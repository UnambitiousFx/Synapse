using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnambitiousFx.Synapse.Endpoints.Generator.Emit;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator;

/// <summary>
///     Emits endpoint registration and route metadata for every endpoint declared in the compilation.
/// </summary>
/// <remarks>
///     Discovery walks the base-type chain of every class declaration (<see cref="CreateSyntaxProvider" />)
///     rather than matching an attribute. An endpoint may declare its route inside <c>Configure</c> and
///     carry no attribute at all, so attribute-based discovery (which caches better) would miss it.
/// </remarks>
[Generator]
public sealed class EndpointsGenerator : IIncrementalGenerator
{
    private const string EndpointVoid = "UnambitiousFx.Synapse.Endpoints.Endpoint`1";
    private const string EndpointValue = "UnambitiousFx.Synapse.Endpoints.Endpoint`2";
    private const string EndpointMapped = "UnambitiousFx.Synapse.Endpoints.MappedEndpoint`4";
    private const string EndpointStream = "UnambitiousFx.Synapse.Endpoints.StreamEndpoint`2";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Analyze(ctx))
            .Where(static target => target is not null)
            .Select(static (target, _) => target!.Value)
            .Collect();

        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value);
            return value;
        });

        context.RegisterSourceOutput(
            endpoints.Combine(rootNamespace),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static EndpointTarget? Analyze(GeneratorSyntaxContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol symbol ||
            symbol.IsAbstract)
        {
            return null;
        }

        for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var metadataName =
                $"{baseType.OriginalDefinition.ContainingNamespace}.{baseType.OriginalDefinition.MetadataName}";

            var kind = metadataName switch
            {
                EndpointVoid => EndpointKind.Void,
                EndpointValue => EndpointKind.Value,
                EndpointMapped => EndpointKind.Mapped,
                EndpointStream => EndpointKind.Stream,
                _ => (EndpointKind?)null
            };

            if (kind is null)
            {
                continue;
            }

            var bound = baseType.TypeArguments[0];
            var (method, route) = ReadRouteAttribute(symbol);

            EquatableArray<BindablePropertyModel> boundProperties;
            bool hasParameterlessConstructor;
            EquatableArray<string> primaryConstructorParameterNames;

            if (bound is INamedTypeSymbol boundNamedType)
            {
                boundProperties = CollectBindableProperties(boundNamedType, method, route);
                (hasParameterlessConstructor, primaryConstructorParameterNames) =
                    ResolveConstructionStrategy(boundNamedType);
            }
            else
            {
                boundProperties = new EquatableArray<BindablePropertyModel>(Array.Empty<BindablePropertyModel>());
                hasParameterlessConstructor = true;
                primaryConstructorParameterNames = new EquatableArray<string>(Array.Empty<string>());
            }

            return new EndpointTarget(
                symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                bound.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                kind.Value,
                method,
                route,
                ReadGroupAttribute(symbol),
                LocationInfo.CreateFrom(symbol.Locations.FirstOrDefault()),
                boundProperties,
                hasParameterlessConstructor,
                primaryConstructorParameterNames);
        }

        return null;
    }

    /// <summary>
    ///     Resolves every bindable property of <paramref name="boundType" />, applying the five
    ///     binding-source resolution rules from spec section 4, in order:
    ///     <list type="number">
    ///         <item><c>[NotBound]</c> excludes the property entirely.</item>
    ///         <item>
    ///             <c>[FromRoute]</c>/<c>[FromQuery]</c>/<c>[FromHeader]</c>/<c>[FromBody]</c> pins the
    ///             source, with the key taken from the attribute's name or else the property name.
    ///         </item>
    ///         <item>A name matching a route parameter (case-insensitively) binds from the route.</item>
    ///         <item>A bodyless verb (<c>GET</c>/<c>DELETE</c>/<c>HEAD</c>) binds from the query.</item>
    ///         <item>Otherwise the property binds from the body.</item>
    ///     </list>
    ///     A property whose type has no viable parse path (not <see cref="string" />, not an enum,
    ///     and with no two-argument <c>TryParse(string, out T)</c>), or that can be assigned neither
    ///     via a settable property nor via a record <c>with</c> expression, is omitted rather than
    ///     turned into code that would not compile — Task 17's SYNE012 and SYNE011 report those cases
    ///     as diagnostics instead.
    /// </summary>
    private static EquatableArray<BindablePropertyModel> CollectBindableProperties(INamedTypeSymbol boundType,
        string httpMethod,
        string route)
    {
        // Keyed case-insensitively (route matching ignores case) but valued with the template's own
        // casing, so a matched property reads the route value under the name the route declares it
        // by, not the property's own PascalCase spelling.
        var routeParameters = ExtractRouteParameterNames(route);
        var isBodylessVerb = httpMethod is "GET" or "DELETE" or "HEAD";

        var models = new List<BindablePropertyModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = boundType; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol
                    {
                        IsStatic: false, IsIndexer: false,
                        DeclaredAccessibility: Accessibility.Public or Accessibility.Internal
                    } property)
                {
                    continue;
                }

                if (!seen.Add(property.Name))
                {
                    continue;
                }

                var model = ResolveBindableProperty(property, routeParameters, isBodylessVerb);
                if (model is not null)
                {
                    models.Add(model);
                }
            }
        }

        return new EquatableArray<BindablePropertyModel>(models.ToArray());
    }

    /// <summary>
    ///     Decides how a bodyless binder should construct <paramref name="boundType" />. Most message
    ///     shapes (<c>{ get; init; }</c> properties with no positional parameters) have an implicit
    ///     parameterless constructor, so <c>new T()</c> plus property assignment works. A positional
    ///     record (or any type whose only accessible constructor takes parameters) has no
    ///     parameterless constructor at all — <c>new T()</c> for it is <c>CS7036</c> — so the binder
    ///     must instead call the constructor with the most parameters (the closest analogue to "the
    ///     primary constructor" for an arbitrary type), matching each parameter to a property by name
    ///     at emit time.
    /// </summary>
    private static (bool HasParameterlessConstructor, EquatableArray<string> PrimaryConstructorParameterNames)
        ResolveConstructionStrategy(INamedTypeSymbol boundType)
    {
        var accessibleConstructors = boundType.Constructors
            .Where(c => !c.IsStatic && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .ToArray();

        var none = new EquatableArray<string>(Array.Empty<string>());

        if (accessibleConstructors.Length == 0 ||
            accessibleConstructors.Any(c => c.Parameters.Length == 0))
        {
            // No accessible constructor at all is out of scope here (nothing this emitter does can
            // construct such a type; that needs its own diagnostic) — the pre-existing `new T()`
            // fallback is used either way, same as when a parameterless constructor genuinely exists.
            return (true, none);
        }

        // Deterministic tie-break: most parameters wins; ties broken by the parameter names
        // themselves rather than by declaration order, which the compiler does not guarantee is
        // stable across equivalent-looking source.
        var primary = accessibleConstructors
            .OrderByDescending(c => c.Parameters.Length)
            .ThenBy(c => string.Join(",", c.Parameters.Select(p => p.Name)), StringComparer.Ordinal)
            .First();

        var parameterNames = primary.Parameters.Select(p => p.Name).ToArray();
        return (false, new EquatableArray<string>(parameterNames));
    }

    private static BindablePropertyModel? ResolveBindableProperty(IPropertySymbol property,
        Dictionary<string, string> routeParameters,
        bool isBodylessVerb)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();

            if (attributeName == "UnambitiousFx.Synapse.Endpoints.NotBoundAttribute")
            {
                return null;
            }
        }

        var (source, sourceKey) = ResolveSource(property, routeParameters, isBodylessVerb);
        if (source is null)
        {
            return null;
        }

        var (canAssign, isRecordWith) = ResolveAssignmentStrategy(property);
        if (!canAssign)
        {
            // SYNE011 (Task 17): an init-only property on a non-record cannot be assigned after the
            // body is deserialized. Omit it rather than emit a `with` expression that will not compile.
            return null;
        }

        var (underlying, isNullable) = UnwrapNullable(property.Type);
        var isString = underlying.SpecialType == SpecialType.System_String;
        var isEnum = underlying.TypeKind == TypeKind.Enum;

        if (!isString && !isEnum && !HasTwoArgumentTryParse(underlying))
        {
            // SYNE012 (Task 17): no viable parse path for this type. Omit rather than emit a
            // `TryParse` call that will not compile.
            return null;
        }

        return new BindablePropertyModel(
            property.Name,
            underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            source.Value,
            sourceKey!,
            isNullable,
            isString,
            isEnum,
            isRecordWith);
    }

    private static (BindingSource? Source, string? SourceKey) ResolveSource(IPropertySymbol property,
        Dictionary<string, string> routeParameters,
        bool isBodylessVerb)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();

            switch (attributeName)
            {
                case "Microsoft.AspNetCore.Mvc.FromRouteAttribute":
                    return (BindingSource.Route, ReadAttributeName(attribute) ?? property.Name);
                case "Microsoft.AspNetCore.Mvc.FromQueryAttribute":
                    return (BindingSource.Query, ReadAttributeName(attribute) ?? property.Name);
                case "UnambitiousFx.Synapse.Endpoints.FromHeaderAttribute":
                    return (BindingSource.Header, ReadHeaderName(attribute) ?? property.Name);
                case "Microsoft.AspNetCore.Mvc.FromBodyAttribute":
                    return (BindingSource.Body, property.Name);
            }
        }

        if (routeParameters.TryGetValue(property.Name, out var routeName))
        {
            return (BindingSource.Route, routeName);
        }

        return isBodylessVerb
            ? (BindingSource.Query, property.Name)
            : (BindingSource.Body, property.Name);
    }

    private static string? ReadAttributeName(AttributeData attribute)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == "Name" && pair.Value.Value is string name)
            {
                return name;
            }
        }

        return null;
    }

    private static string? ReadHeaderName(AttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments;
        return arguments.Length == 1 ? arguments[0].Value as string : null;
    }

    private static (bool CanAssign, bool IsRecordWith) ResolveAssignmentStrategy(IPropertySymbol property)
    {
        var setter = property.SetMethod;
        if (setter is null ||
            setter.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected
                or Accessibility.ProtectedAndInternal)
        {
            // The generated binder is a sibling class, not a subclass, so a setter only reachable
            // from within the type (private) or from a derived type (protected / private protected)
            // is not assignable from generated code.
            return (false, false);
        }

        if (!setter.IsInitOnly)
        {
            return (true, false);
        }

        // Init-only: only assignable through a `with` expression, which only records support.
        return (property.ContainingType.IsRecord, property.ContainingType.IsRecord);
    }

    private static (ITypeSymbol Underlying, bool IsNullable) UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            return (nullable.TypeArguments[0], true);
        }

        return (type, type.NullableAnnotation == NullableAnnotation.Annotated);
    }

    private static bool HasTwoArgumentTryParse(ITypeSymbol type)
    {
        foreach (var member in type.GetMembers("TryParse"))
        {
            if (member is not IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public } method)
            {
                continue;
            }

            if (method.Parameters.Length != 2)
            {
                continue;
            }

            var first = method.Parameters[0];
            var second = method.Parameters[1];

            if (first.Type.SpecialType == SpecialType.System_String &&
                second.RefKind == RefKind.Out &&
                SymbolEqualityComparer.Default.Equals(second.Type, type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Extracts route-parameter names from a template such as <c>/things/{thingId:guid}</c>,
    ///     stripping constraints, default values, the optional marker, and any catch-all prefix.
    /// </summary>
    private static Dictionary<string, string> ExtractRouteParameterNames(string route)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        while (index < route.Length)
        {
            var open = route.IndexOf('{', index);
            if (open < 0)
            {
                break;
            }

            var close = route.IndexOf('}', open + 1);
            if (close < 0)
            {
                break;
            }

            var inner = route.Substring(open + 1, close - open - 1);
            index = close + 1;

            // Catch-all: {*name} or {**name}.
            inner = inner.TrimStart('*');

            var end = inner.Length;
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] is ':' or '?' or '=')
                {
                    end = i;
                    break;
                }
            }

            var name = inner.Substring(0, end);
            if (name.Length > 0)
            {
                names[name] = name;
            }
        }

        return names;
    }

    private static (string Method, string Route) ReadRouteAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                if (type.ToDisplayString() != "UnambitiousFx.Synapse.Endpoints.HttpEndpointAttribute")
                {
                    continue;
                }

                // Verb attributes pass the method to the base constructor, so the derived
                // attribute's single argument is the route.
                var arguments = attribute.ConstructorArguments;
                if (arguments.Length == 1)
                {
                    var verb = attribute.AttributeClass!.Name switch
                    {
                        "GetAttribute" => "GET",
                        "PostAttribute" => "POST",
                        "PutAttribute" => "PUT",
                        "PatchAttribute" => "PATCH",
                        "DeleteAttribute" => "DELETE",
                        _ => string.Empty
                    };
                    return (verb, arguments[0].Value as string ?? string.Empty);
                }

                if (arguments.Length == 2)
                {
                    var method = (arguments[0].Value as string ?? string.Empty).ToUpperInvariant();
                    return (method, arguments[1].Value as string ?? string.Empty);
                }
            }
        }

        // No attribute: the route is declared in Configure.
        return (string.Empty, string.Empty);
    }

    private static string? ReadGroupAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { IsGenericType: true } generic &&
                $"{generic.ContainingNamespace}.{generic.OriginalDefinition.MetadataName}" ==
                "UnambitiousFx.Synapse.Endpoints.InGroupAttribute`1")
            {
                return generic.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return null;
    }

    private static void Emit(SourceProductionContext context,
        ImmutableArray<EndpointTarget> endpoints,
        string? rootNamespace)
    {
        if (endpoints.IsDefaultOrEmpty)
        {
            return;
        }

        var ns = rootNamespace ?? "UnambitiousFx.Synapse.Endpoints.Generated";
        var ordered = endpoints.OrderBy(e => e.EndpointFullName, StringComparer.Ordinal).ToArray();

        // Several endpoints can bind the same message type, but EndpointRegistry.RegisterBinder is
        // keyed by the message type, so only one binder is emitted per distinct bound type: the
        // group's first endpoint by EndpointFullName (see `ordered` above) wins, and that endpoint's
        // own route/verb resolution is what the shared binder uses — silently, for every other
        // endpoint bound to the same type. See EndpointTarget.BoundProperties for the known
        // limitation this creates and the diagnostic planned to report it (SYNE013). The resulting
        // array is then re-ordered by bound-type name purely for deterministic emission order.
        var boundTypes = ordered
            .GroupBy(e => e.BoundTypeFullName, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                var isBodylessVerb = first.HttpMethod is "GET" or "DELETE" or "HEAD";
                return new BoundTypeInfo(first.BoundTypeFullName, first.BoundProperties, isBodylessVerb,
                    first.HasParameterlessConstructor, first.PrimaryConstructorParameterNames);
            })
            .OrderBy(t => t.TypeFullName, StringComparer.Ordinal)
            .ToArray();

        context.AddSource("EndpointGroup.g.cs", EndpointGroupEmitter.EmitGroup(ns, ordered));
        context.AddSource("SynapseEndpointRegistrations.g.cs",
            EndpointGroupEmitter.EmitRegistrations(ns, ordered, boundTypes));
        context.AddSource("SynapseEndpointBinders.g.cs", BinderEmitter.Emit(ns, boundTypes));
    }
}
