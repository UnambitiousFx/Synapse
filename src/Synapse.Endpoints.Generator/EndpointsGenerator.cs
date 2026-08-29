using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnambitiousFx.Synapse.Endpoints.Generator.Diagnostics;
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
    private const string RawEndpointFree = "UnambitiousFx.Synapse.Endpoints.RawEndpoint";
    private const string RawEndpointVoid = "UnambitiousFx.Synapse.Endpoints.RawEndpoint`1";
    private const string RawEndpointValue = "UnambitiousFx.Synapse.Endpoints.RawEndpoint`2";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var analyzed = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Analyze(ctx))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!.Value)
            .Collect();

        // The MSBuild RootNamespace is the source of truth for the namespace to emit into, and can
        // differ from the assembly name. It must be checked with IsNullOrWhiteSpace, not `??`: a
        // project declaring <RootNamespace></RootNamespace> surfaces the property as *present but
        // empty*, which a null check never catches and which emitted a literal `namespace ;` into all
        // three generated files (CS1001, plus a cascading CS0234). Falls back to the assembly's own
        // name, matching Synapse.Generator — a namespace a consumer would plausibly have typed,
        // unlike a hardcoded one.
        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Combine(context.CompilationProvider)
            .Select(static (pair, _) =>
            {
                var (provider, compilation) = pair;
                if (provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                var fromAssembly = compilation.GetRootNamespaceFromAssemblyAttributes();

                // Last resort: an unnamed compilation would otherwise put us right back at
                // `namespace ;`. Unreachable from a real project build, kept so it stays unreachable.
                return string.IsNullOrWhiteSpace(fromAssembly)
                    ? "UnambitiousFx.Synapse.Endpoints.Generated"
                    : fromAssembly;
            });

        // SYNE008 needs every [JsonSerializable(typeof(X))] registration in the compilation, which
        // is a compilation-wide fact, not a per-candidate one — a single CompilationProvider step
        // computes it once rather than re-walking the whole reference graph from inside Analyze for
        // every candidate class. See CollectJsonSerializableRegistrations for the reference-graph
        // trade-off this makes (the same one Task 14 accepted for CreateSyntaxProvider).
        var jsonContext = context.CompilationProvider
            .Select(static (compilation, _) => CollectJsonSerializableRegistrations(compilation));

        context.RegisterSourceOutput(
            analyzed.Combine(rootNamespace).Combine(jsonContext),
            static (spc, pair) => Emit(spc, pair.Left.Left, pair.Left.Right, pair.Right));
    }

    private static EndpointAnalysisResult? Analyze(GeneratorSyntaxContext context)
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
                RawEndpointFree => EndpointKind.Raw,
                RawEndpointVoid => EndpointKind.RawVoid,
                RawEndpointValue => EndpointKind.RawValue,
                _ => (EndpointKind?)null
            };

            if (kind is null)
            {
                continue;
            }

            // The free-form low level is not generic: it binds nothing, so there is no bound type to
            // resolve, no binder to generate and no binding diagnostic that could apply to it.
            var bound = baseType.TypeArguments.Length > 0
                ? baseType.TypeArguments[0]
                : null;

            // SYNE008: the type actually written back as the response body, which differs from
            // `bound` for Mapped (THttpResponse, not the internal TRequest/TResponse pair) and does
            // not exist at all for Void (204 No Content has no body to serialize). Stream's wire
            // type is IAsyncEnumerable<TItem> — the exact type StreamEndpoint.CreateDescriptor
            // declares via ProducesResponseMetadata and the type Microsoft.AspNetCore.OpenApi asks
            // the resolver chain for — not bare TItem, which used to let a stream endpoint's
            // response slip past this check with only its item type registered (a false negative:
            // build stays warning-free, /openapi/v1.json 500s at runtime for real).
            ITypeSymbol? responseType = kind switch
            {
                EndpointKind.Value or EndpointKind.RawValue => baseType.TypeArguments[1],
                EndpointKind.Mapped => baseType.TypeArguments[3],
                EndpointKind.Stream => WrapInAsyncEnumerable(context.SemanticModel.Compilation, baseType.TypeArguments[1]),
                _ => null
            };

            var (method, route) = ReadRouteAttribute(symbol);
            var (groupFullName, groupType) = ReadGroupAttribute(symbol);
            var location = LocationInfo.CreateFrom(symbol.Locations.FirstOrDefault());
            var classDeclaration = (ClassDeclarationSyntax)context.Node;

            var diagnostics = new List<DiagnosticInfo>();

            // SYNE010 first: a shape violation makes every other diagnostic moot — the endpoint
            // cannot be mapped at all regardless of anything else found below.
            if (TryDescribeShapeViolation(symbol, out var shapeViolation))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.InvalidEndpointShape,
                    location,
                    new EquatableArray<string>([symbol.ToDisplayString(), shapeViolation])));
            }

            // SYNE006
            if (groupType is not null && !DerivesFromEndpointGroup(groupType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.InvalidGroupType,
                    location,
                    new EquatableArray<string>(
                        [groupType.ToDisplayString(), symbol.ToDisplayString()])));
            }

            EquatableArray<BindablePropertyModel> boundProperties;
            bool hasParameterlessConstructor;
            EquatableArray<ConstructorParameterModel> primaryConstructorParameters;

            if (kind.Value.HasGeneratedBinder() && bound is INamedTypeSymbol boundNamedType)
            {
                // SYNE002, SYNE007, SYNE011, SYNE012 (Task 17) are found and reported while resolving
                // properties, alongside SYNE001 below.
                boundProperties = CollectBindableProperties(boundNamedType, method, route, diagnostics,
                    out var hasConventionBoundProperty);
                (hasParameterlessConstructor, primaryConstructorParameters) =
                    ResolveConstructionStrategy(boundNamedType, context.SemanticModel.Compilation,
                        boundProperties);

                // SYNE001
                CheckRouteParameters(boundProperties, boundNamedType.ToDisplayString(), route, location, diagnostics);

                // SYNE014 — the endpoint declares its route (and therefore its verb) in Configure, so
                // IsBodylessVerb had to assume a bodyless verb to resolve binding sources at all, and
                // at least one property's source actually came from that assumption. Scoped to
                // convention-bound properties on purpose: an endpoint whose every property carries an
                // explicit [FromRoute]/[FromQuery]/[FromHeader]/[FromBody] has no ambiguity left to
                // warn about, so it stays silent.
                if (method.Length == 0 && hasConventionBoundProperty)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        EndpointDiagnostics.RouteInConfigureWithConventionBinding,
                        location,
                        new EquatableArray<string>([symbol.ToDisplayString(), boundNamedType.ToDisplayString()])));
                }
            }
            else
            {
                boundProperties = new EquatableArray<BindablePropertyModel>(Array.Empty<BindablePropertyModel>());
                hasParameterlessConstructor = true;
                primaryConstructorParameters =
                    new EquatableArray<ConstructorParameterModel>(Array.Empty<ConstructorParameterModel>());
            }

            // SYNE005 — only Endpoint<TRequest> / Endpoint<TRequest,TResponse> dispatch a single
            // response; StreamEndpoint and MappedEndpoint are unaffected (Mapped's bound type is the
            // HTTP DTO, not the dispatched message, so this check does not apply to it).
            if (kind.Value.DispatchesKnownMessage() && bound is not null && ImplementsStreamRequest(bound))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.StreamMessageOnNonStreamEndpoint,
                    location,
                    new EquatableArray<string>([bound.ToDisplayString()])));
            }

            // SYNE009
            if (method.Length > 0 && ConfigureCallsVerbMethodDirectly(classDeclaration))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.RouteDeclaredTwice,
                    location,
                    new EquatableArray<string>([symbol.ToDisplayString()])));
            }

            var overridesOnSuccess = DeclaresOnSuccessOverride(symbol);
            var callsDeclarativeSuccessMethod = ConfigureCallsSuccessMethodDirectly(classDeclaration);

            // SYNE003 — only Endpoint<TRequest,TResponse> actually returns a value; Mapped maps
            // through its own ToResponse/OnSuccess pair and is out of scope for this nudge.
            if (kind.Value.ReturnsValue() &&
                method is "POST" or "PUT" &&
                !overridesOnSuccess &&
                !callsDeclarativeSuccessMethod)
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.NoExplicitSuccessMapping,
                    location,
                    new EquatableArray<string>([symbol.ToDisplayString(), method])));
            }

            // SYNE004 — the declarative call silently wins over the override at dispatch time
            // (EndpointConfiguration.SuccessMapper is checked before OnSuccess), regardless of kind.
            if (overridesOnSuccess && callsDeclarativeSuccessMethod)
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.ConflictingSuccessMapping,
                    location,
                    new EquatableArray<string>([symbol.ToDisplayString()])));
            }

            // SYNE008: which of this endpoint's request/response types are actually JSON-relevant,
            // resolved here (once per endpoint) and reported later, once per distinct missing type,
            // from Emit — see ReportMissingJsonRegistrations.
            // Only a generated binder deserializes the request body from a type the generator can
            // see. A hand-written BindAsync may read any type it likes, or none — those call sites are
            // checked separately, by scanning the endpoint's own body-reading calls.
            var jsonRequestTypeName = kind.Value.HasGeneratedBinder() && bound is not null
                ? ResolveJsonRequestTypeName(bound, method, boundProperties)
                : null;
            var jsonResponseTypeName = ResolveJsonResponseTypeName(responseType);

            // SYNE008 for the low level: its JSON-relevant types are not on a base class, so they are
            // read off the endpoint's own call sites instead.
            var jsonCallSites = kind.Value.HasGeneratedBinder()
                ? new EquatableArray<JsonCallSite>(Array.Empty<JsonCallSite>())
                : CollectJsonCallSites(classDeclaration, context.SemanticModel);

            var diagnosticInfos = new EquatableArray<DiagnosticInfo>(diagnostics.ToArray());

            // Every Error-severity diagnostic blocks this endpoint's own emission (nulls `target`
            // below) EXCEPT SYNE011 and SYNE012, which are deliberately excluded: the property they
            // report is already omitted from boundProperties, and the rest of the endpoint generates
            // working code around that omission (see ResolveBindableProperty). Gating on them anyway
            // would only suppress a correctly-generated binder alongside the diagnostic that explains
            // why one property is missing from it. So SYNE001/SYNE002/SYNE005/SYNE006/SYNE009/SYNE010
            // block; SYNE007/SYNE011/SYNE012/SYNE013 (Warning, or excluded here) do not.
            //
            // This check keys off `DefaultSeverity` — the descriptor's built-in severity — not the
            // *effective* severity a consumer may have reconfigured via .editorconfig
            // (`dotnet_diagnostic.SYNEnnn.severity`). That has a real, asymmetric consequence: a
            // consumer who downgrades a *blocking* diagnostic (e.g. SYNE002 to Warning) still gets the
            // whole `EndpointTarget` nulled here — the endpoint silently fails to register at runtime,
            // with no compile error to explain it, precisely because they downgraded the diagnostic
            // that would have told them why. Downgrading SYNE011/SYNE012 instead changes nothing about
            // gating (they never blocked in the first place) — the consumer still gets a compiling
            // endpoint with one property skipped, a strictly gentler failure mode. This asymmetry is
            // accepted as-is (not a bug to fix): SYNE001/SYNE002/etc. represent shapes this generator
            // has decided are not safe to emit code for at all, and reconfiguring their severity is a
            // deliberate override of that decision, made with the same responsibility as reconfiguring
            // any other "treat as blocking" analyzer rule.
            var hasBlockingError = diagnostics.Exists(static d =>
                d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error &&
                d.Descriptor.Id is not ("SYNE011" or "SYNE012"));

            EndpointTarget? target = hasBlockingError
                ? null
                : new EndpointTarget(
                    symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    bound?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
                    kind.Value,
                    method,
                    route,
                    groupFullName,
                    location,
                    boundProperties,
                    hasParameterlessConstructor,
                    primaryConstructorParameters,
                    jsonRequestTypeName,
                    jsonResponseTypeName,
                    jsonCallSites);

            return new EndpointAnalysisResult(target, diagnosticInfos);
        }

        return null;
    }

    /// <summary>
    ///     SYNE010: an endpoint class that <c>MapEndpoint&lt;TEndpoint&gt;()</c> — constrained
    ///     <c>where TEndpoint : EndpointBase, new()</c> — cannot be instantiated for. All three
    ///     reasons are checked (rather than stopping at the first) so the message names every shape
    ///     problem the class actually has.
    /// </summary>
    private static bool TryDescribeShapeViolation(INamedTypeSymbol symbol, out string reason)
    {
        var reasons = new List<string>();

        if (symbol.TypeParameters.Length > 0)
        {
            reasons.Add("is generic");
        }

        for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (containing.TypeParameters.Length > 0)
            {
                reasons.Add("is nested inside a generic type");
                break;
            }
        }

        if (!HasPublicParameterlessConstructor(symbol))
        {
            reasons.Add("has no public parameterless constructor");
        }

        reason = string.Join(" and ", reasons);
        return reasons.Count > 0;
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol symbol)
    {
        foreach (var constructor in symbol.Constructors)
        {
            if (!constructor.IsStatic &&
                constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility == Accessibility.Public)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>SYNE005: whether <paramref name="type" /> implements <c>IStreamRequest&lt;T&gt;</c> for any <c>T</c>.</summary>
    private static bool ImplementsStreamRequest(ITypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            var metadataName =
                $"{iface.OriginalDefinition.ContainingNamespace}.{iface.OriginalDefinition.MetadataName}";
            if (metadataName == "UnambitiousFx.Synapse.Abstractions.IStreamRequest`1")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>SYNE006: whether <paramref name="type" /> derives from <c>EndpointGroup</c>.</summary>
    private static bool DerivesFromEndpointGroup(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var metadataName = $"{current.ContainingNamespace}.{current.MetadataName}";
            if (metadataName == "UnambitiousFx.Synapse.Endpoints.EndpointGroup")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     SYNE009: whether <paramref name="classDeclaration" /> declares its own <c>Configure</c>
    ///     method that invokes <c>Get</c>/<c>Post</c>/<c>Put</c>/<c>Patch</c>/<c>Delete</c>/<c>Route</c>
    ///     directly on its builder parameter. Deliberately limited to this direct case — a call
    ///     reached through a helper method or a captured local is not detected; see
    ///     <see cref="EndpointDiagnostics.RouteDeclaredTwice" /> for why.
    /// </summary>
    private static bool ConfigureCallsVerbMethodDirectly(ClassDeclarationSyntax classDeclaration)
    {
        return ConfigureCallsMethodDirectly(classDeclaration, VerbMethodNames);
    }

    private static readonly string[] VerbMethodNames = ["Get", "Post", "Put", "Patch", "Delete", "Route"];

    /// <summary>
    ///     SYNE003/SYNE004: the declarative success methods on <c>IEndpointBuilder</c>/
    ///     <c>IEndpointBuilder&lt;TResponse&gt;</c> — each sets <c>EndpointConfiguration.SuccessMapper</c>,
    ///     which is checked before <c>OnSuccess</c> at dispatch time.
    /// </summary>
    private static readonly string[] DeclarativeSuccessMethodNames = ["Ok", "Created", "Accepted", "NoContent", "StatusCode"];

    /// <summary>
    ///     SYNE003/SYNE004: whether <paramref name="classDeclaration" /> declares its own
    ///     <c>Configure</c> method that invokes one of <see cref="DeclarativeSuccessMethodNames" />
    ///     directly on its builder parameter. Same direct-case-only limitation as
    ///     <see cref="ConfigureCallsVerbMethodDirectly" /> — see
    ///     <see cref="EndpointDiagnostics.NoExplicitSuccessMapping" /> and
    ///     <see cref="EndpointDiagnostics.ConflictingSuccessMapping" /> for why.
    /// </summary>
    private static bool ConfigureCallsSuccessMethodDirectly(ClassDeclarationSyntax classDeclaration)
    {
        return ConfigureCallsMethodDirectly(classDeclaration, DeclarativeSuccessMethodNames);
    }

    /// <summary>
    ///     Whether <paramref name="classDeclaration" /> declares its own <c>Configure</c> method
    ///     that invokes one of <paramref name="methodNames" /> directly on its builder parameter —
    ///     that is, <c>builder.Name(...)</c>, not <c>builder.Other(...).Name(...)</c>. Shared by
    ///     SYNE009 (verb methods) and SYNE003/SYNE004 (declarative success methods): all three
    ///     diagnostics accept the same direct-case-only limitation.
    /// </summary>
    private static bool ConfigureCallsMethodDirectly(ClassDeclarationSyntax classDeclaration,
        IReadOnlyCollection<string> methodNames)
    {
        var configureMethod = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(static m => m.Identifier.Text == "Configure" && m.ParameterList.Parameters.Count == 1);

        if (configureMethod is null)
        {
            return false;
        }

        var parameterName = configureMethod.ParameterList.Parameters[0].Identifier.Text;

        SyntaxNode? body = configureMethod.Body;
        body ??= configureMethod.ExpressionBody;
        if (body is null)
        {
            return false;
        }

        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax identifier
                } memberAccess &&
                identifier.Identifier.Text == parameterName &&
                methodNames.Contains(memberAccess.Name.Identifier.Text))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     SYNE004: whether <paramref name="symbol" /> declares its own <c>OnSuccess</c> override —
    ///     checked against the symbol's own member list rather than syntax, which is robust to the
    ///     method being expression-bodied, block-bodied, or spread across partial declarations.
    ///     Applies to both the <c>OnSuccess(TResponse, HttpContext)</c> and
    ///     <c>OnSuccess(HttpContext)</c> overloads (<c>Endpoint&lt;TRequest,TResponse&gt;</c> and
    ///     <c>Endpoint&lt;TRequest&gt;</c> respectively) — the name alone is enough, since only an
    ///     endpoint base class declares a virtual member by this name for a derived class to override.
    /// </summary>
    private static bool DeclaresOnSuccessOverride(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers("OnSuccess"))
        {
            if (member is IMethodSymbol { IsOverride: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     SYNE001: reports every route-template parameter that no resolved <see cref="BindablePropertyModel" />
    ///     actually binds from the route.
    /// </summary>
    /// <remarks>
    ///     Deliberately checks the already-resolved <paramref name="boundProperties" /> rather than
    ///     raw property names: a property with <c>[FromRoute(Name = "...")]</c> can bind a route
    ///     parameter under a completely different property name (see
    ///     <c>BinderEmissionEdgeCaseTests.Generate_ForMvcBindingAttributes_...</c>), and a property
    ///     whose name happens to match but that resolution excluded — an explicit
    ///     <c>[FromQuery]</c> override, or no viable <c>TryParse</c> (Task 17's SYNE012) — is not a
    ///     "matching bindable property" regardless of the name coincidence.
    /// </remarks>
    private static void CheckRouteParameters(EquatableArray<BindablePropertyModel> boundProperties,
        string boundTypeDisplayName,
        string route,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics)
    {
        var routeParameters = ExtractRouteParameterNames(route);
        if (routeParameters.Count == 0)
        {
            return;
        }

        var routeSourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in boundProperties)
        {
            if (property.Source == BindingSource.Route)
            {
                routeSourceKeys.Add(property.SourceKey);
            }
        }

        foreach (var pair in routeParameters)
        {
            if (!routeSourceKeys.Contains(pair.Key))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.RouteParameterHasNoProperty,
                    location,
                    new EquatableArray<string>([pair.Value, boundTypeDisplayName])));
            }
        }
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
    ///         <item>
    ///             A bodyless verb (<c>GET</c>/<c>DELETE</c>/<c>HEAD</c>/<c>OPTIONS</c>/<c>TRACE</c>,
    ///             or an endpoint declaring its route in <c>Configure</c> and so carrying no verb at
    ///             all — see <see cref="IsBodylessVerb" />) binds from the query.
    ///         </item>
    ///         <item>Otherwise the property binds from the body.</item>
    ///     </list>
    ///     A property whose type has no viable parse path (not <see cref="string" />, not an enum,
    ///     and with no two-argument <c>TryParse(string, out T)</c>), or that can be assigned neither
    ///     via a settable property nor via a record <c>with</c> expression, is omitted rather than
    ///     turned into code that would not compile — SYNE012 and SYNE011 report those cases as
    ///     diagnostics instead (Task 17), scoped to route/query/header-bound properties only; a
    ///     <c>[FromBody]</c> property is populated by JSON-deserializing the whole message in one
    ///     shot, so neither check applies to it. SYNE002 (two properties claiming one input) and
    ///     SYNE007 (an explicit <c>[FromBody]</c> property on a bodyless verb) are also found here,
    ///     once the full set of resolved properties for this endpoint's own route and verb is known.
    /// </summary>
    private static EquatableArray<BindablePropertyModel> CollectBindableProperties(INamedTypeSymbol boundType,
        string httpMethod,
        string route,
        List<DiagnosticInfo> diagnostics,
        out bool hasConventionBoundProperty)
    {
        // Keyed case-insensitively (route matching ignores case) but valued with the template's own
        // casing, so a matched property reads the route value under the name the route declares it
        // by, not the property's own PascalCase spelling.
        var routeParameters = ExtractRouteParameterNames(route);
        var isBodylessVerb = IsBodylessVerb(httpMethod);
        var boundTypeDisplay = boundType.ToDisplayString();

        hasConventionBoundProperty = false;

        var models = new List<BindablePropertyModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var propertyLocations = new Dictionary<string, LocationInfo?>(StringComparer.Ordinal);
        var explicitFromBodyProperties = new List<string>();

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

                var propertyLocation = LocationInfo.CreateFrom(property.Locations.FirstOrDefault());

                var model = ResolveBindableProperty(property, routeParameters, isBodylessVerb, propertyLocation,
                    diagnostics, out var isConventionBound);
                if (model is null)
                {
                    continue;
                }

                models.Add(model);
                propertyLocations[model.Name] = propertyLocation;
                hasConventionBoundProperty |= isConventionBound;

                if (model.Source == BindingSource.Body)
                {
                    // SYNE007: an explicit [FromBody] property on a bodyless verb can never bind — a
                    // GET/DELETE/HEAD request carries no body at runtime regardless of what the
                    // generated code attempts to read. Convention alone never produces Body on a
                    // bodyless verb (see ResolveSource), so reaching Body here while isBodylessVerb is
                    // true always means an explicit [FromBody] forced it (Rule 1: explicit wins).
                    // Gated on the *declared* verb, not the assumed one: an endpoint that declares its
                    // route in Configure has no verb here at all, and reporting "'' requests never
                    // carry a body" against a verb nobody wrote would be both meaningless to read and
                    // wrong for a computed POST. SYNE014 covers that endpoint shape instead.
                    if (IsDeclaredBodylessVerb(httpMethod))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            EndpointDiagnostics.BodyOnlyPropertyOnBodylessVerb,
                            propertyLocation,
                            new EquatableArray<string>([property.Name, boundTypeDisplay, httpMethod])));
                    }

                    if (HasExplicitFromBodyAttribute(property))
                    {
                        explicitFromBodyProperties.Add(property.Name);
                    }
                }
            }
        }

        // SYNE002 — route/query key collisions: two properties resolved to the same (Source,
        // SourceKey) pair, keyed case-insensitively the same way route/query matching itself is.
        foreach (var sourceGroup in models
                     .Where(static m => m.Source is BindingSource.Route or BindingSource.Query)
                     .GroupBy(static m => m.Source))
        {
            var sourceLabel = sourceGroup.Key == BindingSource.Route ? "route parameter" : "query key";

            foreach (var keyGroup in sourceGroup.GroupBy(static m => m.SourceKey, StringComparer.OrdinalIgnoreCase))
            {
                var conflicting = keyGroup.Select(static m => m.Name).ToArray();
                if (conflicting.Length <= 1)
                {
                    continue;
                }

                ReportInputClaimConflict(conflicting, boundTypeDisplay, $"{sourceLabel} '{keyGroup.Key}'",
                    propertyLocations[conflicting[0]], diagnostics);
            }
        }

        // SYNE002 — more than one explicit [FromBody]: unlike route/query, [FromBody]'s SourceKey is
        // always the property's own name (see ResolveSource), so this case can never be found by the
        // (Source, SourceKey) grouping above and needs its own check.
        if (explicitFromBodyProperties.Count > 1)
        {
            ReportInputClaimConflict(explicitFromBodyProperties, boundTypeDisplay,
                "the request body (more than one [FromBody])", propertyLocations[explicitFromBodyProperties[0]],
                diagnostics);
        }

        return new EquatableArray<BindablePropertyModel>(models.ToArray());
    }

    /// <summary>SYNE002: reports that every property in <paramref name="propertyNames" /> claims <paramref name="inputDescription" />.</summary>
    private static void ReportInputClaimConflict(IEnumerable<string> propertyNames,
        string boundTypeDisplay,
        string inputDescription,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics)
    {
        var joinedNames = string.Join(", ", propertyNames.Select(static n => $"'{n}'"));
        diagnostics.Add(new DiagnosticInfo(
            EndpointDiagnostics.PropertiesClaimSameInput,
            location,
            new EquatableArray<string>([joinedNames, boundTypeDisplay, inputDescription])));
    }

    /// <summary>Whether <paramref name="property" /> carries an explicit MVC <c>[FromBody]</c> attribute.</summary>
    private static bool HasExplicitFromBodyAttribute(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.FromBodyAttribute")
            {
                return true;
            }
        }

        return false;
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
    private static (bool HasParameterlessConstructor, EquatableArray<ConstructorParameterModel> PrimaryConstructorParameters)
        ResolveConstructionStrategy(INamedTypeSymbol boundType,
            Compilation compilation,
            EquatableArray<BindablePropertyModel> boundProperties)
    {
        var compilationAssembly = compilation.Assembly;

        // Internal counts as accessible only within the assembly being compiled: an internal
        // constructor on a message type from a referenced assembly is not callable from the generated
        // binder, and treating it as callable emitted `new T()` for CS1729. See
        // docs/known-issues/058.
        var accessibleConstructors = boundType.Constructors
            .Where(c => !c.IsStatic &&
                        (c.DeclaredAccessibility == Accessibility.Public ||
                         (c.DeclaredAccessibility == Accessibility.Internal &&
                          SymbolEqualityComparer.Default.Equals(c.ContainingAssembly, compilationAssembly))))
            .ToArray();

        var none = new EquatableArray<ConstructorParameterModel>(Array.Empty<ConstructorParameterModel>());

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

        var parameters = primary.Parameters
            .Select(p => new ConstructorParameterModel(
                p.Name,
                p.Type.IsReferenceType,
                MatchProperty(boundType, boundProperties, compilation, p),
                FormatDefaultValue(p)))
            .ToArray();
        return (false, new EquatableArray<ConstructorParameterModel>(parameters));
    }

    /// <summary>
    ///     Resolves which bindable property, if any, supplies a constructor parameter's argument.
    /// </summary>
    /// <remarks>
    ///     Names are matched case-insensitively, as a positional record's parameter and its property
    ///     differ only in case. The type check is the point: a name match alone is not enough, because
    ///     the argument is passed from a local whose type is the property's, and passing it has to
    ///     compile. A parameter left unmatched here falls back to its default (or <c>default</c>) and
    ///     the property is applied after construction instead, which is always available — every
    ///     bindable property is either settable or on a record, as SYNE011 guarantees.
    /// </remarks>
    private static string? MatchProperty(INamedTypeSymbol boundType,
        EquatableArray<BindablePropertyModel> boundProperties,
        Compilation compilation,
        IParameterSymbol parameter)
    {
        string? bindableName = null;
        foreach (var property in boundProperties)
        {
            if (string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
            {
                bindableName = property.Name;
                break;
            }
        }

        if (bindableName is null)
        {
            return null;
        }

        // The local the emitter passes has the property's own type, annotation included, so that is
        // what has to be convertible — not the underlying type the model carries as a string.
        foreach (var member in boundType.GetMembers(bindableName))
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            var conversion = compilation.ClassifyCommonConversion(property.Type, parameter.Type);
            if (!conversion.IsIdentity && !conversion.IsImplicit)
            {
                // int? -> int, for instance: CS1503 if emitted.
                return null;
            }

            // A nullable reference into a non-nullable parameter compiles but warns (CS8604), which
            // fails a TreatWarningsAsErrors build on generated code the consumer cannot edit.
            if (property.Type.IsReferenceType &&
                property.Type.NullableAnnotation == NullableAnnotation.Annotated &&
                parameter.Type.NullableAnnotation == NullableAnnotation.NotAnnotated)
            {
                return null;
            }

            return bindableName;
        }

        return null;
    }

    /// <summary>
    ///     Renders a constructor parameter's default value as a C# expression, or
    ///     <see langword="null" /> when it has no default.
    /// </summary>
    /// <remarks>
    ///     Cast to the parameter's own type rather than emitted bare, because a primitive literal does
    ///     not always assign to the type that declared it: <c>float f = 1.5f</c> round-trips through
    ///     <see cref="SymbolDisplay.FormatPrimitive" /> as <c>1.5</c>, which is a <c>double</c> and
    ///     CS0664 on assignment, and an enum default arrives as its underlying integer. A default the
    ///     compiler cannot express as a constant — <c>Guid g = default</c> — has no
    ///     <see cref="IParameterSymbol.ExplicitDefaultValue" />, so it becomes the <c>default</c>
    ///     keyword, which is correct for every type.
    /// </remarks>
    private static string? FormatDefaultValue(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return null;
        }

        var value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            return parameter.Type.IsReferenceType ||
                   parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";
        }

        var literal = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
        var target = parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                     parameter.Type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
            ? nullable.TypeArguments[0]
            : parameter.Type;

        return $"({target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({literal})";
    }

    /// <summary>
    ///     Whether <paramref name="httpMethod" /> is a verb the endpoint's binder should treat as
    ///     carrying no request body, so unannotated properties resolve to the query string (rule 4)
    ///     rather than the body (rule 5) and no <c>ReadJsonBodyAsync</c> call is emitted.
    /// </summary>
    /// <remarks>
    ///     An <em>empty</em> method — an endpoint that declares its route in <c>Configure</c> and so
    ///     carries no route attribute for the generator to read a verb from — counts as bodyless.
    ///     That is a deliberate assumption, not a certainty: the verb is a runtime value the
    ///     generator cannot see. It is the right default because a computed route is overwhelmingly a
    ///     <c>GET</c>, and because getting it wrong the other way is catastrophic rather than
    ///     cosmetic — before this, such an endpoint emitted a body read and <em>every</em> request to
    ///     it failed (500 with no <c>Content-Type</c>, 400 with <c>Content-Length: 0</c>). A computed
    ///     <c>POST</c> route is the case the assumption is wrong for, and SYNE014 exists to say so
    ///     out loud whenever any property's source actually came from this assumption rather than
    ///     from an explicit <c>[From*]</c> attribute — see
    ///     <see cref="EndpointDiagnostics.RouteInConfigureWithConventionBinding" />.
    /// </remarks>
    private static bool IsBodylessVerb(string httpMethod)
    {
        return httpMethod.Length == 0 || IsDeclaredBodylessVerb(httpMethod);
    }

    /// <summary>
    ///     Whether <paramref name="httpMethod" /> is an explicitly declared verb that conventionally
    ///     carries no request body. Unlike <see cref="IsBodylessVerb" /> an empty method is
    ///     <see langword="false" /> here: "no verb was declared" is not the same claim as "a verb
    ///     that never carries a body was declared", and diagnostics that name the verb in their
    ///     message (SYNE007) must not fire off the assumption.
    /// </summary>
    /// <remarks>
    ///     <c>OPTIONS</c> and <c>TRACE</c> join <c>GET</c>/<c>DELETE</c>/<c>HEAD</c>: neither carries
    ///     a request body per RFC 9110 (TRACE is forbidden one outright), and the docs actively point
    ///     at <c>[HttpEndpoint("OPTIONS", …)]</c> as the way to declare such an endpoint, which until
    ///     now emitted a body read for it. <c>POST</c>/<c>PUT</c>/<c>PATCH</c> stay body-carrying.
    ///     Kept structurally identical to the runtime's
    ///     <c>UnambitiousFx.Synapse.Endpoints.Internal.HttpMethodHelpers</c>, which makes the same
    ///     GET/DELETE/HEAD/OPTIONS/TRACE call for OpenAPI <c>Accepts</c> metadata; the two cannot
    ///     share code (this project targets netstandard2.0 and does not reference the runtime
    ///     assembly), so they are kept in sync by hand and each points at the other.
    /// </remarks>
    private static bool IsDeclaredBodylessVerb(string httpMethod)
    {
        return httpMethod is "GET" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE";
    }

    private static BindablePropertyModel? ResolveBindableProperty(IPropertySymbol property,
        Dictionary<string, string> routeParameters,
        bool isBodylessVerb,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        out bool isConventionBound)
    {
        isConventionBound = false;

        foreach (var attribute in property.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();

            if (attributeName == "UnambitiousFx.Synapse.Endpoints.NotBoundAttribute")
            {
                return null;
            }
        }

        var (source, sourceKey, fromConvention) = ResolveSource(property, routeParameters, isBodylessVerb);
        if (source is null)
        {
            return null;
        }

        var (underlying, isNullable) = UnwrapNullable(property.Type);
        var isString = underlying.SpecialType == SpecialType.System_String;
        var isEnum = underlying.TypeKind == TypeKind.Enum;

        // SYNE011/SYNE012 (Task 17) apply only to route/query/header-bound properties. A
        // [FromBody]-sourced property is never assigned or parsed by generated code at all — the
        // whole message is populated in one shot by JSON-deserializing the request body (see
        // BinderEmitter) — so neither an accessible setter nor a TryParse method is required for it,
        // and reporting either diagnostic for one would be a false positive.
        var isRecordWith = false;
        if (source.Value != BindingSource.Body)
        {
            bool canAssign;
            (canAssign, isRecordWith) = ResolveAssignmentStrategy(property);
            if (!canAssign)
            {
                // SYNE011: neither a direct assignment (no setter) nor a `with` expression (not a
                // record) can apply this property's value. Omit it rather than emit code that would
                // not compile — the diagnostic is what makes the omission visible.
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.UnassignableBoundProperty,
                    location,
                    new EquatableArray<string>([property.Name, property.ContainingType.ToDisplayString()])));
                return null;
            }

            if (!isString && !isEnum &&
                !HasTwoArgumentTryParse(underlying) &&
                !HasFormatProviderTryParse(underlying))
            {
                // SYNE012: no viable parse path for this type. Omit rather than emit a `TryParse`
                // call that will not compile. Reported at the exact condition that already decides
                // the omission, rather than a separately-maintained list of "known good" types, so
                // the diagnostic can never disagree with what the emitter actually does.
                //
                // Both TryParse shapes are accepted because the emitter emits both: a type that
                // implements IParsable<T> — the canonical way to write a strongly-typed id, and what
                // ASP.NET Core's own binder looks for — supplies only the three-argument overload.
                // Gating on the two-argument form alone rejected such a type, which cascaded into
                // SYNE001 and suppressed the whole endpoint. See docs/known-issues/057.
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.UnparsableBoundPropertyType,
                    location,
                    new EquatableArray<string>(
                        [property.Name, property.ContainingType.ToDisplayString(), underlying.ToDisplayString()])));
                return null;
            }
        }

        isConventionBound = fromConvention;

        return new BindablePropertyModel(
            property.Name,
            underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            source.Value,
            sourceKey!,
            isNullable,
            isString,
            isEnum,
            isRecordWith,
            HasFormatProviderTryParse(underlying),
            underlying.IsReferenceType,
            property.IsRequired);
    }

    /// <summary>
    ///     Applies the binding-source rules (spec section 4) to one property.
    /// </summary>
    /// <returns>
    ///     The resolved source and key, plus whether the source came from the verb-dependent
    ///     convention (rules 4 and 5) rather than from an explicit <c>[From*]</c> attribute or a
    ///     route-parameter name match. Only the verb-dependent fallback is reported as
    ///     "convention" — a route-name match (rule 3) does not depend on the verb, so a route
    ///     declared in <c>Configure</c> cannot resolve it wrongly; that is what keeps SYNE014
    ///     scoped to properties whose source really did hinge on the assumed verb.
    /// </returns>
    private static (BindingSource? Source, string? SourceKey, bool FromConvention) ResolveSource(
        IPropertySymbol property,
        Dictionary<string, string> routeParameters,
        bool isBodylessVerb)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();

            switch (attributeName)
            {
                case "Microsoft.AspNetCore.Mvc.FromRouteAttribute":
                    return (BindingSource.Route, ReadAttributeName(attribute) ?? property.Name, false);
                case "Microsoft.AspNetCore.Mvc.FromQueryAttribute":
                    return (BindingSource.Query, ReadAttributeName(attribute) ?? property.Name, false);
                case "UnambitiousFx.Synapse.Endpoints.FromHeaderAttribute":
                    return (BindingSource.Header, ReadHeaderName(attribute) ?? property.Name, false);

                // Microsoft's own FromHeader is honoured too, for the same reason FromRoute, FromQuery
                // and FromBody are: it is the attribute a reader already has in scope. Recognising
                // three of the four and silently ignoring the fourth meant a property marked
                // [FromHeader(Name = "If-Match")] from Microsoft.AspNetCore.Mvc fell through to the
                // binding convention and read the *query string* under its property name — no header,
                // no diagnostic. See docs/known-issues/062.
                case "Microsoft.AspNetCore.Mvc.FromHeaderAttribute":
                    return (BindingSource.Header, ReadAttributeName(attribute) ?? property.Name, false);
                case "Microsoft.AspNetCore.Mvc.FromBodyAttribute":
                    return (BindingSource.Body, property.Name, false);
            }
        }

        if (routeParameters.TryGetValue(property.Name, out var routeName))
        {
            return (BindingSource.Route, routeName, false);
        }

        return isBodylessVerb
            ? (BindingSource.Query, property.Name, true)
            : (BindingSource.Body, property.Name, true);
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

    /// <summary>
    ///     Whether <paramref name="type" /> exposes the culture-aware
    ///     <c>TryParse(string, IFormatProvider, out T)</c> — the shape <c>IParsable&lt;T&gt;</c>
    ///     requires, and the one ASP.NET Core's own parameter binding uses so that a wire value never
    ///     depends on the server's locale. Types offering only the two-argument overload (all SYNE012
    ///     insists on) are still bound, through that overload.
    /// </summary>
    private static bool HasFormatProviderTryParse(ITypeSymbol type)
    {
        foreach (var member in type.GetMembers("TryParse"))
        {
            if (member is not IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public } method)
            {
                continue;
            }

            if (method.Parameters.Length != 3)
            {
                continue;
            }

            var first = method.Parameters[0];
            var second = method.Parameters[1];
            var third = method.Parameters[2];

            // Compared by name rather than by display string: the parameter is declared
            // `IFormatProvider?`, so ToDisplayString() carries the nullable annotation and never
            // matches "System.IFormatProvider".
            if (first.Type.SpecialType == SpecialType.System_String &&
                second.Type is INamedTypeSymbol
                {
                    Name: "IFormatProvider",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                } &&
                third.RefKind == RefKind.Out &&
                SymbolEqualityComparer.Default.Equals(third.Type, type))
            {
                return true;
            }
        }

        return false;
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
    ///     SYNE008: the display name of <paramref name="bound" /> (the request/message type), or
    ///     null when it is not actually deserialized from the JSON request body — a bodyless verb
    ///     (GET/DELETE/HEAD) with no property explicitly bound via <c>[FromBody]</c> never reaches
    ///     the JSON deserializer at all (see <c>BinderEmitter</c>'s <c>isBodyless</c>), so requiring
    ///     its registration would be a false positive. Also null for a primitive/framework scalar
    ///     type or a type parameter — see <see cref="IsJsonCheckable" />.
    /// </summary>
    private static string? ResolveJsonRequestTypeName(ITypeSymbol bound,
        string httpMethod,
        EquatableArray<BindablePropertyModel> boundProperties)
    {
        if (!IsJsonCheckable(bound))
        {
            return null;
        }

        if (IsBodylessVerb(httpMethod))
        {
            var hasBodyProperty = false;
            foreach (var property in boundProperties)
            {
                if (property.Source == BindingSource.Body)
                {
                    hasBodyProperty = true;
                    break;
                }
            }

            if (!hasBodyProperty)
            {
                return null;
            }
        }

        return bound.ToDisplayString();
    }

    /// <summary>
    ///     SYNE008: the display name of <paramref name="responseType" />, or null when there is no
    ///     response body to serialize (<see cref="EndpointKind.Void" />, where
    ///     <paramref name="responseType" /> is itself null) or it is a primitive/framework scalar
    ///     type or a type parameter — see <see cref="IsJsonCheckable" />.
    /// </summary>
    private static string? ResolveJsonResponseTypeName(ITypeSymbol? responseType)
    {
        if (responseType is null || !IsJsonCheckable(responseType))
        {
            return null;
        }

        return responseType.ToDisplayString();
    }

    /// <summary>
    ///     SYNE008: constructs <c>IAsyncEnumerable&lt;<paramref name="itemType" />&gt;</c> from the
    ///     compilation's own <c>System.Collections.Generic.IAsyncEnumerable`1</c> definition —
    ///     <c>StreamEndpoint&lt;TRequest, TItem&gt;</c>'s actual wire response type, matching what
    ///     <c>StreamEndpoint.CreateDescriptor</c> declares via
    ///     <c>ProducesResponseMetadata(..., typeof(IAsyncEnumerable&lt;TItem&gt;), ...)</c>. Built
    ///     from the compilation rather than a string-matched metadata name so the result is a real
    ///     constructed <see cref="INamedTypeSymbol" /> that <see cref="IsFrameworkOwned" /> can walk
    ///     into (it already recurses into a constructed generic's type arguments — see that method's
    ///     remarks — so a closed <c>IAsyncEnumerable&lt;ThingDto&gt;</c> comes out not-framework-owned
    ///     exactly like <c>IReadOnlyList&lt;ThingDto&gt;</c> already does). Returns
    ///     <paramref name="itemType" /> itself, unwrapped, if <c>IAsyncEnumerable`1</c> cannot be
    ///     found in the compilation's reference graph — a condition that should not occur for a real
    ///     ASP.NET Core project (the type is part of the BCL every compilation already references),
    ///     but this keeps the generator from ever throwing over it.
    /// </summary>
    private static ITypeSymbol WrapInAsyncEnumerable(Compilation compilation,
        ITypeSymbol itemType)
    {
        var asyncEnumerableDefinition =
            compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");

        return asyncEnumerableDefinition?.Construct(itemType) ?? itemType;
    }

    /// <summary>
    ///     SYNE008: whether <paramref name="type" /> is a candidate that could plausibly need a
    ///     <c>[JsonSerializable(typeof(...))]</c> registration at all. Excludes a type parameter (a
    ///     generic endpoint class is already SYNE010; nothing concrete to check), an error type
    ///     (unresolved symbol — reporting on it would be noise on top of a real compile error), and
    ///     anything <see cref="IsFrameworkOwned" /> considers framework-owned once
    ///     <c>Nullable&lt;T&gt;</c> is unwrapped.
    /// </summary>
    /// <remarks>
    ///     Fix round 1 (Task 18 review) replaced a hardcoded list of "known intrinsically-supported"
    ///     types (<c>string</c>, the numeric primitives, <c>Guid</c>, <c>DateTimeOffset</c>,
    ///     <c>TimeSpan</c>, <c>Uri</c>, ...) with the structural rule in
    ///     <see cref="IsFrameworkOwned" />. A hardcoded list is a losing game — every .NET release adds
    ///     built-in-supported types (<c>Half</c>, <c>Int128</c>/<c>UInt128</c>, ...), <c>Version</c> and
    ///     <c>byte[]</c> were already missing from it, and each omission is a false positive on
    ///     correct code. This deliberately means SYNE008 will not flag <c>ProblemDetails</c> or
    ///     <c>HttpValidationProblemDetails</c> — both framework types, both genuinely needing
    ///     registration under Native AOT. That gap is intentional, not an oversight: SYNE008's scope is
    ///     the endpoint's own declared request/response type, not every type reachable from the
    ///     response pipeline or the OpenAPI document; the <c>ProblemDetails</c>/
    ///     <c>HttpValidationProblemDetails</c> obligation is covered in Task 24's documentation instead.
    /// </remarks>
    private static bool IsJsonCheckable(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
        {
            return false;
        }

        var (underlying, _) = UnwrapNullable(type);
        return !IsFrameworkOwned(underlying);
    }

    /// <summary>
    ///     SYNE008: whether every piece of <paramref name="type" /> is owned by a framework assembly
    ///     (see <see cref="IsFrameworkAssembly" />) — the structural replacement for a hardcoded
    ///     "known intrinsic type" list (see the remarks on <see cref="IsJsonCheckable" />).
    /// </summary>
    /// <remarks>
    ///     An array type recurses into its element type (<c>byte[]</c> is framework-owned because
    ///     <c>byte</c> is). A named type — this is the case that needs care — is framework-owned only
    ///     when *both* its own unbound definition's declaring assembly is a framework assembly *and*
    ///     every one of its type arguments is, recursively, also framework-owned. That second half is
    ///     load-bearing: <c>IReadOnlyList&lt;T&gt;</c>'s own home is a framework assembly
    ///     (<c>System.Private.CoreLib</c>), but <c>IReadOnlyList&lt;ThingDto&gt;</c> must still come out
    ///     as *not* framework-owned when <c>ThingDto</c> is the consumer's own type — checked
    ///     empirically: a constructed generic type's <c>ContainingAssembly</c> is the unbound
    ///     definition's assembly regardless of its type arguments, so testing it directly (without
    ///     also walking the type arguments) would have silently exempted every generic collection of a
    ///     user type from SYNE008, breaking the exact-closed-type collection check this diagnostic
    ///     depends on. Any other type kind reached here (pointer, function pointer, dynamic — never
    ///     realistically an endpoint request/response type) defaults to framework-owned, the same
    ///     silence-biased default every ambiguous case in this diagnostic takes.
    /// </remarks>
    private static bool IsFrameworkOwned(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsFrameworkOwned(arrayType.ElementType);
        }

        if (type is INamedTypeSymbol namedType)
        {
            var declaringAssembly = namedType.OriginalDefinition.ContainingAssembly;
            if (declaringAssembly is null || !IsFrameworkAssembly(declaringAssembly))
            {
                return false;
            }

            foreach (var typeArgument in namedType.TypeArguments)
            {
                if (!IsFrameworkOwned(typeArgument))
                {
                    return false;
                }
            }

            return true;
        }

        return true;
    }

    /// <summary>
    ///     SYNE008: walks every named type reachable from <paramref name="compilation" /> — its own
    ///     declarations *and* every referenced assembly's, with no exclusion at the enumeration level
    ///     — looking for a type deriving from
    ///     <c>System.Text.Json.Serialization.JsonSerializerContext</c>, and collects the type
    ///     argument of every <c>[JsonSerializable(typeof(X))]</c> attribute found on one.
    /// </summary>
    /// <remarks>
    ///     Scanning the whole reference graph, not just this compilation's own syntax trees, is
    ///     deliberate: a consumer commonly defines shared JSON contracts and the
    ///     <c>JsonSerializerContext</c> for them in one project, referenced from several endpoint
    ///     projects, and scanning only the current compilation would false-positive on every one of
    ///     those referencing projects. The cost is the same one <c>EndpointsGenerator</c> already
    ///     accepted for endpoint discovery (<c>CreateSyntaxProvider</c> over attribute-based
    ///     discovery, see the type-level remarks): correctness over the tightest possible
    ///     incremental-caching behaviour. Concretely, this step depends on
    ///     <see cref="IncrementalGeneratorInitializationContext.CompilationProvider" />, which changes
    ///     identity on effectively every keystroke in the IDE (unlike a syntax-tree-scoped provider),
    ///     and re-walks every named type in every referenced assembly — including the BCL and
    ///     ASP.NET Core shared framework — each time it reruns. For a one-shot command-line build
    ///     this is a single walk and immaterial; for IDE responsiveness while typing, it is the most
    ///     expensive step this generator performs. The sibling <c>Synapse.Generator</c>'s
    ///     <c>ExtractAllBehaviorTargets</c> already re-walks the entire *unfiltered*
    ///     <c>GlobalNamespace</c> (BCL included) on every edit for a comparable reason, so this is not
    ///     a new category of cost, and review of this generator confirmed the cost acceptable.
    /// </remarks>
    /// <remarks>
    ///     Fix round 1 (Task 18 review, finding 1) split what had been a single filtered walk into two
    ///     separate questions with two separate filters, after the single-filter version was shown to
    ///     have a compound false-positive path: a consumer with one correctly-named context (opening
    ///     the gate below) plus a second context in a referenced assembly that happens to be named
    ///     <c>System.*</c>/<c>Microsoft.*</c> and legitimately registers one of the consumer's own
    ///     types. Filtering the registered set the same way as the gate dropped that second context's
    ///     registrations while the gate stayed open — reporting a type that actually *is* registered
    ///     as missing. The fix: <em>the gate</em> ("has this consumer opted into source-generated JSON
    ///     at all?") still excludes framework assemblies via <see cref="IsFrameworkAssembly" /> — that
    ///     is the whole reason this filter exists, see the remarks on that method. <em>The registered
    ///     set</em> ("what is already registered?") excludes nothing: every type argument of every
    ///     <c>[JsonSerializable]</c> attribute found on any <c>JsonSerializerContext</c> anywhere in
    ///     the graph counts, framework-named assembly or not, because if any context anywhere
    ///     registers a type, reporting it as missing is simply wrong regardless of what its context's
    ///     assembly happens to be named.
    /// </remarks>
    private static JsonContextInfo CollectJsonSerializableRegistrations(Compilation compilation)
    {
        var contextBaseType =
            compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext");
        if (contextBaseType is null)
        {
            return new JsonContextInfo(false, new EquatableArray<string>(Array.Empty<string>()));
        }

        var hasContext = false;
        var registered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (type, declaringAssembly) in GetAllNamedTypesWithDeclaringAssembly(compilation))
        {
            if (!DerivesFrom(type, contextBaseType))
            {
                continue;
            }

            // The gate excludes framework assemblies; the registered set (below) does not — see the
            // second <remarks> block above for why these are deliberately two different filters.
            // `declaringAssembly` is null for a type declared in the compilation itself (never a
            // framework assembly, regardless of what this project happens to be named), so the gate
            // always opens for the consumer's own declarations.
            if (declaringAssembly is null || !IsFrameworkAssembly(declaringAssembly))
            {
                hasContext = true;
            }

            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() !=
                    "System.Text.Json.Serialization.JsonSerializableAttribute")
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length > 0 &&
                    attribute.ConstructorArguments[0].Value is ITypeSymbol registeredType)
                {
                    registered.Add(registeredType.ToDisplayString());
                }
            }
        }

        // Sorted so that a HashSet's unspecified enumeration order can never change the resulting
        // EquatableArray's element order between two otherwise-identical compilations —
        // EquatableArray<T>.Equals is a positional SequenceEqual, so an order difference alone would
        // be a needless incremental-caching miss.
        var sortedRegistered = registered.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        return new JsonContextInfo(hasContext, new EquatableArray<string>(sortedRegistered));
    }

    /// <summary>Whether <paramref name="type" /> derives from <paramref name="baseCandidate" /> anywhere in its base chain.</summary>
    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseCandidate)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseCandidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Every named type declared in <paramref name="compilation" />'s own source (paired with a
    ///     null assembly) and in every assembly it references (paired with that assembly, so the
    ///     caller can apply its own — possibly different — filter per use; see
    ///     <see cref="CollectJsonSerializableRegistrations" />'s two <c>remarks</c> blocks for why one
    ///     filter does not fit both of that method's questions). No exclusion happens in this method
    ///     itself: it is deliberately the unfiltered whole reference graph a consumer could plausibly
    ///     have put a <c>JsonSerializerContext</c> in, not just the source being compiled.
    /// </summary>
    /// <remarks>
    ///     Deliberately walks <c>compilation.Assembly.GlobalNamespace</c> (scoped to just this
    ///     compilation's own source) for the first half, not <c>compilation.GlobalNamespace</c> —
    ///     the latter is the namespace <em>merged across the whole reference graph already</em>
    ///     (that is how <c>compilation.GlobalNamespace.GetMembers("System")</c> reaches
    ///     <c>System.Console</c> without any explicit reference walk), so using it here would
    ///     silently re-include every referenced assembly's types, defeating any filter a caller
    ///     applies based on the assembly paired with each type. This was found empirically, not
    ///     reasoned out in advance: an early version of this method used
    ///     <c>compilation.GlobalNamespace</c> for both halves and an assembly-name filter applied
    ///     around the second half appeared to have no effect at all.
    /// </remarks>
    private static IEnumerable<(INamedTypeSymbol Type, IAssemblySymbol? DeclaringAssembly)>
        GetAllNamedTypesWithDeclaringAssembly(Compilation compilation)
    {
        foreach (var type in GetAllNamedTypes(compilation.Assembly.GlobalNamespace))
        {
            yield return (type, null);
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                foreach (var type in GetAllNamedTypes(assembly.GlobalNamespace))
                {
                    yield return (type, assembly);
                }
            }
        }
    }

    /// <summary>
    ///     Whether <paramref name="assembly" /> is part of the .NET runtime or the ASP.NET Core shared
    ///     framework, based on a plain assembly-name-prefix match — not a directory/path check (more
    ///     portable across install layouts and single-file/self-contained deployments, where on-disk
    ///     paths are less predictable). Used only to gate <see cref="JsonContextInfo.HasContext" /> in
    ///     <see cref="CollectJsonSerializableRegistrations" /> (see that method's second
    ///     <c>remarks</c> block for why it is <em>not</em> also applied to the registered-type set).
    ///     Added after discovering, empirically, that <c>Microsoft.AspNetCore.App</c> alone ships
    ///     eleven internal <c>JsonSerializerContext</c>-derived types of its own (for example
    ///     <c>Microsoft.AspNetCore.Http.ProblemDetailsJsonContext</c> and
    ///     <c>Microsoft.AspNetCore.Identity.Data.IdentityEndpointsJsonSerializerContext</c>) — without
    ///     this filter, <c>HasContext</c> would be true for essentially every ASP.NET Core application
    ///     regardless of whether that application itself had opted into source-generated JSON, and
    ///     none of those framework contexts register any of the application's own types, so SYNE008
    ///     would fire on almost every endpoint's response type in an application that never asked for
    ///     this check. Accepts the small, deliberate risk of also excluding a legitimately-named
    ///     third-party assembly that happens to start with one of these prefixes from opening the
    ///     gate on its own — a false negative (that assembly's context alone would not open the gate),
    ///     never a false positive, which is the direction this diagnostic is biased.
    /// </summary>
    private static bool IsFrameworkAssembly(IAssemblySymbol assembly)
    {
        var name = assembly.Identity.Name;
        return name.StartsWith("System.", StringComparison.Ordinal) ||
               name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
               name is "netstandard" or "mscorlib" or "WindowsBase";
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var nested in GetAllNamedTypesIncludingNested(type))
            {
                yield return nested;
            }
        }

        foreach (var nestedNamespace in ns.GetNamespaceMembers())
        {
            foreach (var type in GetAllNamedTypes(nestedNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypesIncludingNested(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var t in GetAllNamedTypesIncludingNested(nested))
            {
                yield return t;
            }
        }
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

    /// <summary>
    ///     Reads <c>[InGroup&lt;T&gt;]</c>, returning both the fully-qualified display name used by
    ///     emission and the underlying symbol, which SYNE006 needs to check whether <c>T</c> actually
    ///     derives from <c>EndpointGroup</c>.
    /// </summary>
    private static (string? GroupFullName, INamedTypeSymbol? GroupType) ReadGroupAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { IsGenericType: true } generic &&
                $"{generic.ContainingNamespace}.{generic.OriginalDefinition.MetadataName}" ==
                "UnambitiousFx.Synapse.Endpoints.InGroupAttribute`1")
            {
                var groupType = generic.TypeArguments[0] as INamedTypeSymbol;
                return (generic.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), groupType);
            }
        }

        return (null, null);
    }

    private static void Emit(SourceProductionContext context,
        ImmutableArray<EndpointAnalysisResult> results,
        string rootNamespace,
        JsonContextInfo jsonContext)
    {
        if (results.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var result in results)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }

        var endpoints = results
            .Where(static r => r.Target is not null)
            .Select(static r => r.Target!.Value)
            .ToImmutableArray();

        if (endpoints.IsEmpty)
        {
            return;
        }

        var ns = rootNamespace;
        var ordered = endpoints.OrderBy(e => e.EndpointFullName, StringComparer.Ordinal).ToArray();

        // SYNE008 — reported once per distinct missing type, not once per endpoint, anchored at
        // whichever endpoint (in the same deterministic order used everywhere else in this method)
        // uses it first.
        ReportMissingJsonRegistrations(context, ordered, jsonContext);

        // Several endpoints can bind the same message type, but EndpointRegistry.RegisterBinder is
        // keyed by the message type, so only one binder is emitted per distinct bound type: the
        // group's first endpoint by EndpointFullName (see `ordered` above) wins, and that endpoint's
        // own route/verb resolution is what the shared binder uses — silently, for every other
        // endpoint bound to the same type. See EndpointTarget.BoundProperties for the known
        // limitation this creates; SYNE013 (Task 17), reported just below, is the diagnostic for it.
        // The resulting array is then re-ordered by bound-type name purely for deterministic emission
        // order.
        // Raw endpoints are registered and mapped like any other, but they have no generated binder,
        // so they must not reach the grouping — an empty BoundTypeFullName would otherwise become a
        // group of its own and emit a binder for nothing.
        var typeGroups = ordered
            .Where(e => e.Kind.HasGeneratedBinder())
            .GroupBy(e => e.BoundTypeFullName, StringComparer.Ordinal)
            .ToArray();

        ReportConflictingBindingShapes(context, typeGroups);

        var boundTypes = typeGroups
            .Select(g =>
            {
                var first = g.First();
                var isBodylessVerb = IsBodylessVerb(first.HttpMethod);
                return new BoundTypeInfo(first.BoundTypeFullName, first.BoundProperties, isBodylessVerb,
                    first.HasParameterlessConstructor, first.PrimaryConstructorParameters);
            })
            .OrderBy(t => t.TypeFullName, StringComparer.Ordinal)
            .ToArray();

        context.AddSource("SynapseEndpointGroup.g.cs", EndpointGroupEmitter.EmitGroup(ns, ordered));
        context.AddSource("SynapseEndpointRegistrations.g.cs",
            EndpointGroupEmitter.EmitRegistrations(ns, ordered, boundTypes));
        context.AddSource("SynapseEndpointBinders.g.cs", BinderEmitter.Emit(ns, boundTypes));
    }

    /// <summary>
    ///     SYNE013: reports, once per bound type, when two or more endpoints sharing that type
    ///     resolved different <see cref="BindablePropertyModel" /> sets for it. Comparing the
    ///     resolved property sets — not the endpoints' raw routes or verbs — is deliberate: two
    ///     endpoints with different-looking routes or verbs can still resolve to the exact same
    ///     bindings (for instance, two bodyless verbs where nothing matches either route template),
    ///     in which case the shared binder is correct for both and there is nothing to warn about.
    /// </summary>
    private static void ReportConflictingBindingShapes(SourceProductionContext context,
        IEnumerable<IGrouping<string, EndpointTarget>> typeGroups)
    {
        foreach (var group in typeGroups)
        {
            var endpoints = group.ToArray();
            var distinctBindings = endpoints.Select(static e => e.BoundProperties).Distinct().ToArray();
            if (distinctBindings.Length <= 1)
            {
                continue;
            }

            var first = endpoints[0];
            var endpointNames = string.Join(", ", endpoints.Select(static e => e.EndpointFullName));

            context.ReportDiagnostic(Diagnostic.Create(
                EndpointDiagnostics.ConflictingBindingShapes,
                first.Location?.ToLocation() ?? Location.None,
                first.BoundTypeFullName,
                endpointNames));
        }
    }

    /// <summary>
    ///     SYNE008: the JSON-relevant types a low-level endpoint names in its own body — the type
    ///     argument of a <c>BodyAsync&lt;T&gt;</c> read and of each
    ///     <c>Accepts&lt;T&gt;</c>/<c>Produces&lt;T&gt;</c> declaration.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Scans only invocations written lexically inside the endpoint class. A body read behind a
    ///         helper method on another type is invisible here, and stays a runtime failure under Native
    ///         AOT rather than a build warning — a deliberate limit, not an oversight: following calls
    ///         across types would mean a whole-program walk on every keystroke, and the check exists to
    ///         catch the ordinary case cheaply.
    ///     </para>
    ///     <para>
    ///         Matched on the containing type's full name and the method name rather than on symbol
    ///         identity, so it keeps working for the extension-method call syntax
    ///         (<c>context.BodyAsync&lt;T&gt;()</c>) and the static form alike.
    ///     </para>
    /// </remarks>
    private static EquatableArray<JsonCallSite> CollectJsonCallSites(ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel)
    {
        List<JsonCallSite>? callSites = null;

        foreach (var invocation in classDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
                method.TypeArguments.Length != 1)
            {
                continue;
            }

            var owner = method.ContainingType?.ToDisplayString();
            var isTracked = (owner, method.Name) switch
            {
                ("UnambitiousFx.Synapse.Endpoints.Binding.HttpContextBindingExtensions", "BodyAsync") => true,
                ("UnambitiousFx.Synapse.Endpoints.Builders.IRawEndpointBuilder", "Accepts") => true,
                ("UnambitiousFx.Synapse.Endpoints.Builders.IRawEndpointBuilder", "Produces") => true,
                _ => false
            };

            if (!isTracked)
            {
                continue;
            }

            var argument = method.TypeArguments[0];
            if (!IsJsonCheckable(argument))
            {
                continue;
            }

            callSites ??= [];
            callSites.Add(new JsonCallSite(
                argument.ToDisplayString(),
                LocationInfo.CreateFrom(invocation.GetLocation())));
        }

        return new EquatableArray<JsonCallSite>(callSites?.ToArray() ?? Array.Empty<JsonCallSite>());
    }

    /// <summary>
    ///     SYNE008: reports, once per distinct type absent from <paramref name="jsonContext" />'s
    ///     registrations, every <see cref="EndpointTarget.JsonRequestTypeName" /> and
    ///     <see cref="EndpointTarget.JsonResponseTypeName" /> across <paramref name="orderedEndpoints" />
    ///     that is missing. Nothing is reported at all when <see cref="JsonContextInfo.HasContext" />
    ///     is false — an app with no <c>JsonSerializerContext</c> anywhere in its reference graph has
    ///     not opted into source-generated JSON, so this advice does not apply to it.
    /// </summary>
    private static void ReportMissingJsonRegistrations(SourceProductionContext context,
        EndpointTarget[] orderedEndpoints,
        JsonContextInfo jsonContext)
    {
        if (!jsonContext.HasContext)
        {
            return;
        }

        var registered = new HashSet<string>(jsonContext.RegisteredTypeNames, StringComparer.Ordinal);
        var alreadyReported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var endpoint in orderedEndpoints)
        {
            foreach (var candidate in new[] { endpoint.JsonRequestTypeName, endpoint.JsonResponseTypeName })
            {
                if (candidate is null || registered.Contains(candidate) || !alreadyReported.Add(candidate))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointDiagnostics.MissingJsonSerializableRegistration,
                    endpoint.Location?.ToLocation() ?? Location.None,
                    candidate));
            }

            // A low-level endpoint's types come from its own call sites, so the diagnostic is anchored
            // at the call rather than at the class declaration.
            foreach (var callSite in endpoint.JsonCallSites)
            {
                if (registered.Contains(callSite.TypeName) || !alreadyReported.Add(callSite.TypeName))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointDiagnostics.MissingJsonSerializableRegistration,
                    callSite.Location?.ToLocation() ?? endpoint.Location?.ToLocation() ?? Location.None,
                    callSite.TypeName));
            }
        }
    }
}
