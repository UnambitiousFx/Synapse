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
    private const string FormFileTypeName = "Microsoft.AspNetCore.Http.IFormFile";
    private const string FormFileCollectionTypeName = "Microsoft.AspNetCore.Http.IFormFileCollection";

    private const string EndpointVoid = "UnambitiousFx.Synapse.Endpoints.Endpoint`1";
    private const string EndpointValue = "UnambitiousFx.Synapse.Endpoints.Endpoint`2";
    private const string EndpointContract = "UnambitiousFx.Synapse.Endpoints.ContractEndpoint`4";
    private const string EndpointContractVoid = "UnambitiousFx.Synapse.Endpoints.ContractEndpoint`2";
    private const string EndpointStream = "UnambitiousFx.Synapse.Endpoints.StreamEndpoint`2";
    private const string InlineVoid = "UnambitiousFx.Synapse.Endpoints.InlineEndpoint`1";
    private const string InlineValue = "UnambitiousFx.Synapse.Endpoints.InlineEndpoint`2";
    private const string RawEndpointFree = "UnambitiousFx.Synapse.Endpoints.RawEndpoint";
    private const string BoundEndpointVoid = "UnambitiousFx.Synapse.Endpoints.BoundEndpoint`1";
    private const string BoundEndpointValue = "UnambitiousFx.Synapse.Endpoints.BoundEndpoint`2";

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
                EndpointContract => EndpointKind.Contract,
                EndpointContractVoid => EndpointKind.ContractVoid,
                EndpointStream => EndpointKind.Stream,
                InlineVoid => EndpointKind.InlineVoid,
                InlineValue => EndpointKind.Inline,
                // Last, and it matters: every tier above derives from RawEndpoint, so a chain walk
                // that reached this arm first would classify all of them as the free-form low level.
                RawEndpointFree => EndpointKind.Raw,
                BoundEndpointVoid => EndpointKind.BoundVoid,
                BoundEndpointValue => EndpointKind.BoundValue,
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
            // `bound` for Contract (THttpResponse, not the internal TRequest/TResponse pair) and does
            // not exist at all for Void (204 No Content has no body to serialize). Stream's wire
            // type is IAsyncEnumerable<TItem> — the exact type StreamEndpoint.CreateDescriptor
            // declares via ProducesResponseMetadata and the type Microsoft.AspNetCore.OpenApi asks
            // the resolver chain for — not bare TItem, which used to let a stream endpoint's
            // response slip past this check with only its item type registered (a false negative:
            // build stays warning-free, /openapi/v1.json 500s at runtime for real).
            ITypeSymbol? responseType = kind switch
            {
                EndpointKind.Value or EndpointKind.BoundValue or EndpointKind.Inline =>
                    baseType.TypeArguments[1],
                EndpointKind.Contract => baseType.TypeArguments[3],
                EndpointKind.Stream => WrapInAsyncEnumerable(context.SemanticModel.Compilation, baseType.TypeArguments[1]),
                _ => null
            };

            var (method, route) = ReadRouteAttribute(symbol);
            var (groupFullName, groupType) = ReadGroupAttribute(symbol);
            var location = LocationInfo.CreateFrom(symbol.Locations.FirstOrDefault());
            var classDeclaration = (ClassDeclarationSyntax)context.Node;

            // Every part of the class, not only the matched one: a partial endpoint may write
            // Configure in one file and read its request body in another, and each part carrying a
            // base list is analysed separately, so the syntax scans below have to agree across parts
            // for the surviving analysis (see Emit) to be the same whichever part it came from.
            var declarationParts = ReadDeclarationParts(symbol, classDeclaration);

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

            // SYNE021 — Members, not syntax: an expression-bodied, block-bodied or partial-split
            // declaration all reach the symbol the same way. Matches the tolerance noted at
            // DeclaresOnSuccessOverride above. Only tiers with a generated binder are checked — the
            // three Raw kinds exist precisely so BindAsync can be written by hand.
            if (kind.Value.HasGeneratedBinder() &&
                symbol.GetMembers("BindAsync").Any(static member => member is IMethodSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.BindAsyncIsGenerated,
                    location,
                    new EquatableArray<string>([symbol.Name])));
            }

            // SYNE005 — only Endpoint<TRequest> / Endpoint<TRequest,TResponse> dispatch a single
            // response; StreamEndpoint and ContractEndpoint are unaffected (Contract's bound type is the
            // HTTP DTO, not the dispatched message, so this check does not apply to it).
            if (kind.Value.DispatchesKnownMessage() && bound is not null && ImplementsStreamRequest(bound))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.StreamMessageOnNonStreamEndpoint,
                    location,
                    new EquatableArray<string>([bound.ToDisplayString()])));
            }

            // SYNE009
            if (method.Length > 0 && ConfigureCallsVerbMethodDirectly(declarationParts))
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.RouteDeclaredTwice,
                    location,
                    new EquatableArray<string>([symbol.ToDisplayString()])));
            }

            var overridesOnSuccess = DeclaresOnSuccessOverride(symbol);
            var callsDeclarativeSuccessMethod = ConfigureCallsSuccessMethodDirectly(declarationParts);

            // SYNE003 — only Endpoint<TRequest,TResponse> actually returns a value; Contract maps
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
                ? ResolveJsonRequestTypeName(bound, boundProperties)
                : null;
            var jsonResponseTypeName = ResolveJsonResponseTypeName(responseType);

            // SYNE008 for the low level: its JSON-relevant types are not on a base class, so they are
            // read off the endpoint's own call sites instead.
            var jsonCallSites = kind.Value.HasGeneratedBinder()
                ? new EquatableArray<JsonCallSite>(Array.Empty<JsonCallSite>())
                : CollectJsonCallSites(declarationParts, context.SemanticModel);

            var diagnosticInfos = new EquatableArray<DiagnosticInfo>(diagnostics.ToArray());

            // Every Error-severity diagnostic blocks this endpoint's own emission (nulls `target`
            // below) EXCEPT SYNE011 and SYNE012, which are deliberately excluded: the property they
            // report is already omitted from boundProperties, and the rest of the endpoint generates
            // working code around that omission (see ResolveBindableProperty). Gating on them anyway
            // would only suppress a correctly-generated binder alongside the diagnostic that explains
            // why one property is missing from it. So SYNE001/SYNE002/SYNE005/SYNE006/SYNE009/SYNE010
            // block; SYNE007/SYNE011/SYNE012 (Warning, or excluded here) do not.
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

            var declaration = ReadDeclaration(symbol);

            var endpointFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            EndpointTarget? target = hasBlockingError
                ? null
                : new EndpointTarget(
                    endpointFullName,
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
                    jsonCallSites,
                    declaration);

            return new EndpointAnalysisResult(endpointFullName, target, diagnosticInfos);
        }

        return null;
    }

    /// <summary>
    ///     Reads how the endpoint is declared, so generated code can reopen it.
    /// </summary>
    /// <remarks>
    ///     Every fact here is read from the symbol, or from all of its declaration parts, never from
    ///     the single part that happened to match the syntax predicate. A class declared in several
    ///     parts is analysed once per matching part, and only one of those analyses survives
    ///     deduplication in <see cref="Emit" /> — so a fact read from one part alone (<c>sealed</c>
    ///     written on the part carrying the base list, say) would make the emitted source depend on
    ///     which part won.
    /// </remarks>
    private static EndpointDeclaration ReadDeclaration(INamedTypeSymbol symbol)
    {
        var enclosing = new List<EnclosingTypeDeclaration>();
        var nonPartial = new List<NonPartialEnclosingType>();

        for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
        {
            enclosing.Insert(0, EnclosingTypeDeclaration.From(container));

            if (!IsDeclaredPartial(container))
            {
                nonPartial.Insert(0, new NonPartialEnclosingType(
                    container.Name,
                    LocationInfo.CreateFrom(container.Locations.FirstOrDefault())));
            }
        }

        return new EndpointDeclaration(
            symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            IsDeclaredPartial(symbol),
            symbol.IsSealed,
            EquatableArray<EnclosingTypeDeclaration>.From(enclosing),
            EquatableArray<NonPartialEnclosingType>.From(nonPartial));
    }

    /// <summary>
    ///     Whether <paramref name="symbol" /> is declared <c>partial</c>. Read across every
    ///     declaration part rather than from one of them: <c>partial</c> is not a symbol-level fact,
    ///     but it is a whole-type one — C# requires every part to repeat the keyword.
    /// </summary>
    private static bool IsDeclaredPartial(INamedTypeSymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    }

    /// <summary>
    ///     Every declaration part of <paramref name="symbol" />, ordered by file path then position
    ///     so the scans below see them in the same order on every build.
    /// </summary>
    /// <param name="symbol">The endpoint type.</param>
    /// <param name="matched">The part the syntax predicate matched, returned alone in the common single-part case.</param>
    /// <returns>The endpoint's class declarations.</returns>
    private static ClassDeclarationSyntax[] ReadDeclarationParts(INamedTypeSymbol symbol,
        ClassDeclarationSyntax matched)
    {
        if (symbol.DeclaringSyntaxReferences.Length <= 1)
        {
            return [matched];
        }

        return symbol.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .OrderBy(static declaration => declaration.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.SpanStart)
            .ToArray();
    }

    /// <summary>
    ///     SYNE010: an endpoint class that <c>MapEndpoint&lt;TEndpoint&gt;()</c> — constrained
    ///     <c>where TEndpoint : SynapseEndpoint, new()</c> — cannot be instantiated for. All three
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
    private static bool ConfigureCallsVerbMethodDirectly(ClassDeclarationSyntax[] declarationParts)
    {
        return ConfigureCallsMethodDirectly(declarationParts, VerbMethodNames);
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
    private static bool ConfigureCallsSuccessMethodDirectly(ClassDeclarationSyntax[] declarationParts)
    {
        return ConfigureCallsMethodDirectly(declarationParts, DeclarativeSuccessMethodNames);
    }

    /// <summary>
    ///     Whether <paramref name="classDeclaration" /> declares its own <c>Configure</c> method
    ///     that invokes one of <paramref name="methodNames" /> directly on its builder parameter —
    ///     that is, <c>builder.Name(...)</c>, not <c>builder.Other(...).Name(...)</c>. Shared by
    ///     SYNE009 (verb methods) and SYNE003/SYNE004 (declarative success methods): all three
    ///     diagnostics accept the same direct-case-only limitation.
    /// </summary>
    private static bool ConfigureCallsMethodDirectly(ClassDeclarationSyntax[] declarationParts,
        IReadOnlyCollection<string> methodNames)
    {
        foreach (var declarationPart in declarationParts)
        {
            if (ConfigureCallsMethodDirectly(declarationPart, methodNames))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     <see cref="ConfigureCallsMethodDirectly(ClassDeclarationSyntax[], IReadOnlyCollection{string})" />
    ///     for one declaration part.
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
    ///     Resolves every bindable property of <paramref name="boundType" />, applying the six
    ///     binding-source resolution rules from spec section 4, in order:
    ///     <list type="number">
    ///         <item><c>[NotBound]</c> excludes the property entirely.</item>
    ///         <item>
    ///             <c>[FromRoute]</c>/<c>[FromQuery]</c>/<c>[FromHeader]</c>/<c>[FromBody]</c>/
    ///             <c>[FromForm]</c> pins the source, with the key taken from the attribute's name or
    ///             else the property name.
    ///         </item>
    ///         <item>A name matching a route parameter (case-insensitively) binds from the route.</item>
    ///         <item>
    ///             A bodyless verb (<c>GET</c>/<c>DELETE</c>/<c>HEAD</c>/<c>OPTIONS</c>/<c>TRACE</c>,
    ///             or an endpoint declaring its route in <c>Configure</c> and so carrying no verb at
    ///             all — see <see cref="IsBodylessVerb" />) binds from the query.
    ///         </item>
    ///         <item>Otherwise the property binds from the body.</item>
    ///         <item>
    ///             Unless any property on the message pins itself to the form (rule 2's
    ///             <c>[FromForm]</c>) — see <see cref="HasFormPinnedProperty" /> — in which case rule
    ///             5's outcome flips to the form instead: a request is a form or it is JSON, never
    ///             both.
    ///         </item>
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

        // Answerable only after looking at every property, because any one of them can make the
        // whole message form-bound. Cheap: an attribute scan over the same members the main pass
        // walks.
        var isFormBound = HasFormPinnedProperty(boundType);

        hasConventionBoundProperty = false;

        var models = new List<BindablePropertyModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var propertyLocations = new Dictionary<string, LocationInfo?>(StringComparer.Ordinal);
        var explicitFromBodyProperties = new List<string>();

        // SYNE019 — collected here and reported below, once the full set of resolved properties is
        // known. explicitFromFormProperties gates the diagnostic off the moment any property states
        // its form-binding intent explicitly; formConventionProperties names what actually moved to
        // the form purely as a consequence of rule 6 (isFormBound); formFileProperty is the property
        // whose type made the message form-bound in the first place (rule 3), and is where the
        // diagnostic is reported. Tracked independently of whether ResolveBindableProperty later
        // returns a model for the property (a property can carry [FromForm] and still fail to
        // resolve, e.g. SYNE011/SYNE012), so an explicit annotation always suppresses this diagnostic
        // even on such a property.
        var explicitFromFormProperties = new List<string>();
        var formConventionProperties = new List<string>();
        (string Name, LocationInfo? Location)? formFileProperty = null;

        // SYNE015 — collected here and reported below, because whether the binder reads a body at all
        // is not known until every property has been resolved (an explicit [FromBody] forces a read
        // even on a bodyless verb).
        var unprotectedNotBoundProperties = new List<(string Name, LocationInfo? Location)>();

        for (var type = boundType; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol property || !IsBindableCandidateProperty(property))
                {
                    continue;
                }

                if (!seen.Add(property.Name))
                {
                    continue;
                }

                var propertyLocation = LocationInfo.CreateFrom(property.Locations.FirstOrDefault());

                if (HasExplicitFromFormAttribute(property))
                {
                    explicitFromFormProperties.Add(property.Name);
                }

                if (HasNotBoundAttribute(property))
                {
                    if (!HasSuppressingJsonIgnoreAttribute(property))
                    {
                        unprotectedNotBoundProperties.Add((property.Name, propertyLocation));
                    }

                    continue;
                }

                var model = ResolveBindableProperty(property, routeParameters, isBodylessVerb, isFormBound,
                    propertyLocation, diagnostics, out var isConventionBound);
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

                if (model.Source == BindingSource.Form)
                {
                    // SYNE017: the exact mirror of SYNE007 above — a form-bound property on a
                    // bodyless verb can never bind, since a GET/DELETE/HEAD/OPTIONS/TRACE request
                    // carries no multipart form data at runtime any more than it carries a JSON body.
                    // Gated on the declared verb for the same reason as SYNE007: an endpoint that
                    // declares its route in Configure has no verb here to name.
                    if (IsDeclaredBodylessVerb(httpMethod))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            EndpointDiagnostics.FormPropertyOnBodylessVerb,
                            propertyLocation,
                            new EquatableArray<string>([property.Name, boundTypeDisplay, httpMethod])));
                    }

                    if (model.Shape is BindingValueShape.FormFile or BindingValueShape.FormFileCollection)
                    {
                        // The file property is what makes HasFormPinnedProperty's rule-3 check true in
                        // the first place; the first one found is where SYNE019 (if it fires) is
                        // reported. isConventionBound is always false for a file-shaped property (see
                        // ResolveSource), so it never itself lands in formConventionProperties below.
                        formFileProperty ??= (property.Name, propertyLocation);
                    }
                    else if (isConventionBound)
                    {
                        // SYNE019 — a property that followed the message to the form purely under
                        // rule 6, rather than because it carries its own [FromForm].
                        formConventionProperties.Add(property.Name);
                    }
                }
            }
        }

        // SYNE015 — mirrors BinderEmitter's own hasJsonBodyProperty exactly: the message is
        // JSON-deserialized if and only if some property actually resolved to BindingSource.Body, full
        // stop. Deliberately not "the verb carries a body" — a form-bound message on a POST carries a
        // body too, but rule 6 sends every one of its properties to Form instead of Body (see
        // ResolveSource), so hasJsonBodyProperty is false for it and no JSON read is ever emitted.
        // Keying this on "!isBodylessVerb" as well as "resolved to Body" (rather than on the resolved
        // Body properties alone) previously made this warning fire for exactly that case — a
        // form-bound POST message with a [NotBound] property — even though nothing there is ever
        // JSON-deserialized.
        if (unprotectedNotBoundProperties.Count > 0 &&
            models.Any(static m => m.Source == BindingSource.Body))
        {
            foreach (var (name, propertyLocation) in unprotectedNotBoundProperties)
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.NotBoundPropertyIsStillDeserialized,
                    propertyLocation,
                    new EquatableArray<string>([name, boundTypeDisplay])));
            }
        }

        // SYNE018 — one message binding from both the form and the JSON body. BinderEmitter picks
        // exactly one read strategy for the whole message (hasJsonBodyProperty wins over
        // hasFormProperty — see its own comment), so whichever group loses can never actually
        // populate its properties: a JSON request body has no multipart fields to read from, and a
        // multipart request never carries a JSON body for the deserializer to read either way. This
        // can only happen when an explicit [FromBody] overrides rule 6 on a message that is otherwise
        // form-bound — the unattributed convention fallback in ResolveSource sends every remaining
        // property to Form once isFormBound is true, never to Body.
        var formPropertyNames = models.Where(static m => m.Source == BindingSource.Form)
            .Select(static m => m.Name).ToArray();
        var bodyPropertyNames = models.Where(static m => m.Source == BindingSource.Body)
            .Select(static m => m.Name).ToArray();

        if (formPropertyNames.Length > 0 && bodyPropertyNames.Length > 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                EndpointDiagnostics.FormAndBodyOnOneMessage,
                propertyLocations[formPropertyNames[0]],
                new EquatableArray<string>([
                    boundTypeDisplay,
                    string.Join(", ", formPropertyNames.Select(static n => $"'{n}'")),
                    string.Join(", ", bodyPropertyNames.Select(static n => $"'{n}'"))
                ])));
        }

        // SYNE019 — the message became form-bound purely by inference. isFormBound (computed before
        // the loop by HasFormPinnedProperty) is true, no property anywhere carries an explicit
        // [FromForm], and at least one property actually followed the message to the form under rule
        // 6 rather than staying on the body — so the only way isFormBound could have become true at
        // all is the file-typed property named here (rule 3). A message where the file is the only
        // property never populates formConventionProperties, since there is nothing left to move.
        if (isFormBound && explicitFromFormProperties.Count == 0 && formConventionProperties.Count > 0 &&
            formFileProperty is { } file)
        {
            diagnostics.Add(new DiagnosticInfo(
                EndpointDiagnostics.InferredFormBinding,
                file.Location,
                new EquatableArray<string>([
                    boundTypeDisplay,
                    file.Name,
                    string.Join(", ", formConventionProperties.Select(static n => $"'{n}'"))
                ])));
        }

        // SYNE002 — route/query/form key collisions: two properties resolved to the same (Source,
        // SourceKey) pair, keyed case-insensitively the same way route/query/form matching itself is.
        // Header stays excluded, unchanged from before this diagnostic gained the Form case.
        foreach (var sourceGroup in models
                     .Where(static m => m.Source is BindingSource.Route or BindingSource.Query or BindingSource.Form)
                     .GroupBy(static m => m.Source))
        {
            var sourceLabel = sourceGroup.Key switch
            {
                BindingSource.Route => "route parameter",
                BindingSource.Query => "query key",
                BindingSource.Form => "form field",
                _ => throw new InvalidOperationException($"Unexpected binding source '{sourceGroup.Key}'.")
            };

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
    ///     Whether <paramref name="property" /> carries an explicit <c>[FromForm]</c> attribute — this
    ///     project's own, or Microsoft's. Used by SYNE019, which must stay silent the moment any
    ///     property on the message states its form-binding intent explicitly, whether or not that
    ///     property is itself the one a file-typed property's inference would otherwise be blamed on.
    /// </summary>
    private static bool HasExplicitFromFormAttribute(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name is "UnambitiousFx.Synapse.Endpoints.FromFormAttribute"
                or "Microsoft.AspNetCore.Mvc.FromFormAttribute")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether any property of <paramref name="boundType" /> pins itself to the form, which makes the
    ///     whole message form-bound (rule 6). A file-typed property counts too — rule 3 pins it to the
    ///     form just as surely as an explicit <c>[FromForm]</c> would, and an unattributed
    ///     <c>IFormFile</c> property sitting beside a plain <c>string</c> property must still drag the
    ///     rest of the message onto the form (see
    ///     <c>Generate_ForAnIFormFileProperty_BindsItFromTheFormWithNoAttribute</c>).
    /// </summary>
    private static bool HasFormPinnedProperty(INamedTypeSymbol boundType)
    {
        // Same base-chain walk, same shadowing rule as the main pass: a base-type property
        // redeclared (shadowed) by a derived one must not be counted twice, and must defer to the
        // derived declaration exactly as CollectBindableProperties does.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = boundType; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol property || !IsBindableCandidateProperty(property))
                {
                    continue;
                }

                if (!seen.Add(property.Name))
                {
                    continue;
                }

                if (HasNotBoundAttribute(property))
                {
                    continue;
                }

                if (IsFileTyped(property.Type))
                {
                    return true;
                }

                foreach (var attribute in property.GetAttributes())
                {
                    var name = attribute.AttributeClass?.ToDisplayString();
                    if (name is "UnambitiousFx.Synapse.Endpoints.FromFormAttribute"
                        or "Microsoft.AspNetCore.Mvc.FromFormAttribute")
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="property" /> is a candidate this generator considers for binding
    ///     at all. Deliberately shared by the main property-collection pass
    ///     (<see cref="CollectBindableProperties" />) and its <see cref="HasFormPinnedProperty" />
    ///     pre-pass, rather than each keeping its own copy of the same three checks: a property this
    ///     rejects (static, an indexer, or neither public nor internal) never becomes a bound
    ///     property and never itself resolves to <see cref="BindingSource.Form" />, so it must be
    ///     equally invisible to the rule-6 pre-check that decides whether the *rest* of the message
    ///     flips to the form — otherwise a private <c>[FromForm]</c> property could flip every other
    ///     property on the message to the form while the main pass never saw it at all.
    /// </summary>
    private static bool IsBindableCandidateProperty(IPropertySymbol property)
    {
        return property is { IsStatic: false, IsIndexer: false } &&
               property.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal;
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
    /// <summary>Rule 1: <c>[NotBound]</c> excludes a property from the generated bindings entirely.</summary>
    private static bool HasNotBoundAttribute(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() ==
                "UnambitiousFx.Synapse.Endpoints.NotBoundAttribute")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether the property carries a <c>[JsonIgnore]</c> that actually prevents deserialization.
    /// </summary>
    /// <remarks>
    ///     Only <c>JsonIgnoreCondition.Always</c> — the default when the attribute is written with no
    ///     arguments — stops the serializer setting the property. Every other condition
    ///     (<c>Never</c>, <c>WhenWritingDefault</c>, <c>WhenWritingNull</c>) governs serialization
    ///     only, and leaves the property writable from the request body.
    /// </remarks>
    private static bool HasSuppressingJsonIgnoreAttribute(IPropertySymbol property)
    {
        const int always = 1;

        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() !=
                "System.Text.Json.Serialization.JsonIgnoreAttribute")
            {
                continue;
            }

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Condition")
                {
                    return named.Value.Value is int condition && condition == always;
                }
            }

            return true;
        }

        return false;
    }

    private static bool IsDeclaredBodylessVerb(string httpMethod)
    {
        return httpMethod is "GET" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE";
    }

    private static BindablePropertyModel? ResolveBindableProperty(IPropertySymbol property,
        Dictionary<string, string> routeParameters,
        bool isBodylessVerb,
        bool isFormBound,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        out bool isConventionBound)
    {
        isConventionBound = false;

        var (source, sourceKey, fromConvention) =
            ResolveSource(property, routeParameters, isBodylessVerb, isFormBound);
        if (source is null)
        {
            return null;
        }

        var (underlying, isNullable) = UnwrapNullable(property.Type);

        var shape = BindingValueShape.Scalar;
        var materialization = Materialization.None;

        // A file has no parse step and no JSON representation, so SYNE011 still applies (the value
        // must be assignable) but SYNE012 does not — there is nothing to TryParse from a stream. This
        // sits ahead of the collection-shape check below because a file collection (IFormFile[], say)
        // would otherwise resolve as an ordinary Collection shape with an element type this project's
        // generator cannot parse either, and fail with the wrong diagnostic (SYNE012) instead of
        // reading as files.
        //
        // Source is hardcoded to Form here rather than passed through as `source.Value`, and that is
        // deliberate, not an oversight: an explicit `[FromBody] IFormFile Foo` resolves Source = Body
        // through the attribute switch above, before rule 3 (IsFileTyped) is ever consulted. If that
        // Body source were recorded on the model, BinderEmitter would see hasJsonBodyProperty = true,
        // switch the whole message to JSON-body construction, drop this property from `bindable`
        // (BinderEmitter only reads non-Body properties there) so FormFileValueReadEmitter is never
        // invoked for it at all, and ResolveJsonRequestTypeName would then demand a
        // [JsonSerializable] registration (SYNE008) for a message containing a raw IFormFile — which
        // can never be satisfied and is exactly the Native-AOT violation this feature exists to avoid.
        // The file emitters are selected purely by Shape and never consult Source, so forcing Form
        // costs nothing and keeps "a file-shaped property's Source is never Body" true unconditionally,
        // whatever attribute someone writes on it. (An explicit [FromQuery]/[FromRoute]/[FromHeader]
        // on a file is harmlessly overridden the same way today, and is not a case this needs to
        // preserve.) See Generate_ForAFromBodyIFormFileProperty_StillBindsAsFormNotJson.
        if (TryResolveFileShape(underlying, out var fileShape, out var fileMaterialization))
        {
            var (canAssignFile, isRecordWithFile) = ResolveAssignmentStrategy(property);
            if (!canAssignFile)
            {
                diagnostics.Add(new DiagnosticInfo(
                    EndpointDiagnostics.UnassignableBoundProperty, location,
                    new EquatableArray<string>([property.Name, property.ContainingType.ToDisplayString()])));
                return null;
            }

            isConventionBound = fromConvention;

            return new BindablePropertyModel(
                property.Name, underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                BindingSource.Form, sourceKey!, isNullable, isString: false, isEnum: false,
                isRecordWithFile, parsesWithFormatProvider: false, isReferenceType: true,
                property.IsRequired, fileShape, fileMaterialization);
        }

        // Parseability is tested before collection-ness, so a type that is both enumerable and parsable
        // keeps binding as the scalar it has always been. Reordering these two silently changes that.
        //
        // Route and Body are both excluded: a route segment cannot repeat, so a collection-typed route
        // property has no reader to call (CollectionValueReadEmitter.GetTryGetValuesMethod has no Route
        // arm), and the whole body is already bound by System.Text.Json in one shot. Excluding only Body
        // here left a [FromRoute] collection property resolved as BindingValueShape.Collection, which
        // then threw out of the emitter instead of falling through to SYNE012 below.
        if (!HasViableParsePath(underlying) &&
            source.Value is not (BindingSource.Route or BindingSource.Body) &&
            TryResolveCollectionShape(underlying, out var element, out var resolvedMaterialization))
        {
            shape = BindingValueShape.Collection;
            materialization = resolvedMaterialization;
            underlying = element;
        }

        var isString = underlying.SpecialType == SpecialType.System_String;
        var isEnum = underlying.TypeKind == TypeKind.Enum;

        // SYNE011/SYNE012 (Task 17) apply to every source except Body — which, deliberately, still
        // includes Form: a form *field* needs an accessible setter and a parse path exactly like a
        // query or header value does, unlike a body property. Only a [FromBody]-sourced property is
        // never assigned or parsed by generated code at all — the whole message is populated in one
        // shot by JSON-deserializing the request body (see BinderEmitter) — so neither an accessible
        // setter nor a TryParse method is required for it, and reporting either diagnostic for one
        // would be a false positive. Do not narrow this guard to "Route or Query or Header": that
        // would silently stop reporting SYNE011/SYNE012 for an unassignable or unparsable form field.
        // (A form *file* is exempt from SYNE012 only — see the file-shape branch above, which returns
        // before this check is ever reached for one, having already applied SYNE011 itself.)
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
                // No viable parse path for this type. Omit rather than emit a `TryParse` call that
                // will not compile. Reported at the exact condition that already decides the
                // omission, rather than a separately-maintained list of "known good" types, so the
                // diagnostic can never disagree with what the emitter actually does.
                //
                // Both TryParse shapes are accepted because the emitter emits both: a type that
                // implements IParsable<T> — the canonical way to write a strongly-typed id, and what
                // ASP.NET Core's own binder looks for — supplies only the three-argument overload.
                // Gating on the two-argument form alone rejected such a type, which cascaded into
                // SYNE001 and suppressed the whole endpoint. See docs/known-issues/057.
                //
                // From here the omission is one of two different stories, and only one of them is
                // followable: an unsupported collection shape (SYNE016), whose fix is to change the
                // shape, or an unparsable value (SYNE012), whose fix is to add a TryParse. Route is
                // excluded from the SYNE016 test for the same reason it is excluded from the
                // collection-shape resolution above: a route segment cannot repeat, so a route-bound
                // string[] is not an *unsupported* shape (T[] binds fine over query/header) — it is a
                // supported shape that this source can never read, and telling the author to switch
                // to a shape they are already using would be wrong. Body never reaches this branch at
                // all (the enclosing "if" is scoped to non-Body sources), but is named for the same
                // reason.
                var enumerableElement = shape == BindingValueShape.Scalar &&
                    source.Value is not (BindingSource.Route or BindingSource.Body)
                        ? GetEnumerableElement(underlying)
                        : null;

                if (enumerableElement is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        EndpointDiagnostics.UnsupportedCollectionType,
                        location,
                        new EquatableArray<string>([
                            property.Name,
                            property.ContainingType.ToDisplayString(),
                            underlying.ToDisplayString(),
                            enumerableElement.ToDisplayString()
                        ])));
                    return null;
                }

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
            property.IsRequired,
            shape,
            materialization);
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
        bool isBodylessVerb,
        bool isFormBound)
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

                case "UnambitiousFx.Synapse.Endpoints.FromFormAttribute":
                    return (BindingSource.Form, ReadHeaderName(attribute) ?? property.Name, false);

                // Microsoft's own FromForm is honoured too, for the same reason the other four MVC
                // attributes are: it is the attribute a reader already has in scope, and recognising
                // some of a family and silently ignoring the rest is how docs/known-issues/062
                // happened.
                case "Microsoft.AspNetCore.Mvc.FromFormAttribute":
                    return (BindingSource.Form, ReadAttributeName(attribute) ?? property.Name, false);
            }
        }

        // Rule 3: a file has exactly one source it could possibly come from, so inferring it from the
        // type alone is safe in a way it is not for `string` — unlike a route/query/header ambiguity,
        // there is no other plausible place a file could be read from. Sits ahead of the
        // route-parameter match (an unnamed route segment cannot carry a file anyway) but behind every
        // explicit [From*] attribute above, so an explicit [FromQuery] still wins over it.
        if (IsFileTyped(property.Type))
        {
            return (BindingSource.Form, property.Name, false);
        }

        if (routeParameters.TryGetValue(property.Name, out var routeName))
        {
            return (BindingSource.Route, routeName, false);
        }

        if (isBodylessVerb)
        {
            return (BindingSource.Query, property.Name, true);
        }

        // A request is a form or it is JSON, never both. Once anything on this message is
        // form-bound, the properties that would have come from a JSON body come from the form
        // instead — otherwise every field of a form message would need [FromForm] spelled out on
        // it.
        return isFormBound
            ? (BindingSource.Form, property.Name, true)
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
            // The generated binding lives in the endpoint's class, not in the message's, so a setter
            // only reachable from within the declaring type (private) or from a type derived from it
            // (protected / private protected) is not assignable from generated code.
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
    ///     Whether <paramref name="type" /> is one of the four supported collection shapes, and if so
    ///     over which element and with what materialization.
    /// </summary>
    /// <remarks>
    ///     <c>string</c> is rejected first and explicitly. It is an <c>IEnumerable&lt;char&gt;</c>, so
    ///     without this guard every string property would become a collection of <c>char</c>. The caller
    ///     also tests parseability before calling this (see <see cref="ResolveBindableProperty" />), but
    ///     relying on that ordering alone would make a later reordering silently catastrophic.
    /// </remarks>
    private static bool TryResolveCollectionShape(ITypeSymbol type,
        out ITypeSymbol element,
        out Materialization materialization)
    {
        element = type;
        materialization = Materialization.None;

        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            element = array.ElementType;
            materialization = Materialization.Array;
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            named.TypeArguments.Length == 1)
        {
            var definition = named.OriginalDefinition.ToDisplayString();
            if (definition is "System.Collections.Generic.List<T>"
                or "System.Collections.Generic.IReadOnlyList<T>"
                or "System.Collections.Generic.IEnumerable<T>")
            {
                element = named.TypeArguments[0];
                materialization = Materialization.List;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="type" /> is a file shape, and if so which one, with what
    ///     materialization.
    /// </summary>
    /// <remarks>
    ///     <c>IFormFileCollection</c> is every file on the request; <c>IFormFile[]</c> and the other three
    ///     collection shapes are the files under one field name. That is what ASP.NET Core's own binder
    ///     does, and the distinction belongs here rather than in the emitter.
    /// </remarks>
    private static bool TryResolveFileShape(ITypeSymbol type,
        out BindingValueShape shape,
        out Materialization materialization)
    {
        shape = BindingValueShape.Scalar;
        materialization = Materialization.None;

        var displayName = UnannotatedDisplayString(type);

        if (displayName == FormFileTypeName)
        {
            shape = BindingValueShape.FormFile;
            return true;
        }

        if (displayName == FormFileCollectionTypeName)
        {
            shape = BindingValueShape.FormFileCollection;
            materialization = Materialization.Native;
            return true;
        }

        if (TryResolveCollectionShape(type, out var element, out var elementMaterialization) &&
            UnannotatedDisplayString(element) == FormFileTypeName)
        {
            shape = BindingValueShape.FormFileCollection;
            materialization = elementMaterialization;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     <paramref name="type" />'s display name with any nullable-reference annotation removed, so a
    ///     name comparison answers a question about the type rather than about how it was declared.
    /// </summary>
    /// <remarks>
    ///     <see cref="ISymbol.ToDisplayString(SymbolDisplayFormat)" />'s default format includes
    ///     <c>IncludeNullableReferenceTypeModifier</c>, so <c>IFormFile?</c> renders as
    ///     <c>"Microsoft.AspNetCore.Http.IFormFile?"</c> and never matches a bare interface name.
    ///     <see cref="UnwrapNullable" /> does not help: a nullable *reference* type is the same symbol
    ///     as its non-nullable form and it returns that symbol with the annotation intact. Comparing
    ///     the annotated name is why <c>[FromForm] IFormFile? File</c> reported SYNE012 advising the
    ///     author to implement <c>IParsable&lt;IFormFile?&gt;</c>, and why a bare <c>IFormFile?</c>
    ///     with no attribute fell through rule 6 to the JSON body.
    /// </remarks>
    private static string UnannotatedDisplayString(ITypeSymbol type)
    {
        return type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
    }

    /// <summary>
    ///     Rule 3: whether <paramref name="type" /> is one of the file shapes at all, discarding
    ///     <see cref="TryResolveFileShape" />'s other outputs. A nullable reference (<c>IFormFile?</c>)
    ///     is recognised because <see cref="TryResolveFileShape" /> compares unannotated names — see
    ///     <see cref="UnannotatedDisplayString" />; <see cref="UnwrapNullable" /> alone cannot strip a
    ///     reference type's annotation.
    /// </summary>
    private static bool IsFileTyped(ITypeSymbol type)
    {
        var (underlying, _) = UnwrapNullable(type);
        return TryResolveFileShape(underlying, out _, out _);
    }

    /// <summary>
    ///     The element type of <paramref name="type" />'s <c>IEnumerable&lt;T&gt;</c> implementation, or
    ///     <see langword="null" /> when it implements none. Used only to name the element in SYNE016.
    /// </summary>
    private static ITypeSymbol? GetEnumerableElement(ITypeSymbol type)
    {
        // Defence-in-depth, not load-bearing: the only call site already sits inside an `!isString`
        // branch, so `type` is never actually System_String here. Kept so this method stays correct
        // in isolation if a future caller reuses it without that guard.
        if (type.SpecialType == SpecialType.System_String)
        {
            return null;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            if (candidate.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            {
                return candidate.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>Whether a value of <paramref name="type" /> can be produced from a raw string.</summary>
    private static bool HasViableParsePath(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_String ||
               type.TypeKind == TypeKind.Enum ||
               HasTwoArgumentTryParse(type) ||
               HasFormatProviderTryParse(type);
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
    ///     (GET/DELETE/HEAD) with no property explicitly bound via <c>[FromBody]</c>, or a form-bound
    ///     message, never reaches the JSON deserializer at all (see <c>BinderEmitter</c>'s
    ///     <c>constructsMessage</c>), so requiring its registration would be a false positive. Also
    ///     null for a primitive/framework scalar type or a type parameter — see
    ///     <see cref="IsJsonCheckable" />.
    /// </summary>
    private static string? ResolveJsonRequestTypeName(ITypeSymbol bound,
        EquatableArray<BindablePropertyModel> boundProperties)
    {
        if (!IsJsonCheckable(bound))
        {
            return null;
        }

        // Registration is owed only when the type actually reaches the JSON deserializer, which is
        // when some property binds from the body — the same rule BinderEmitter uses to decide whether
        // to emit the read at all, and it must stay in step with it. The verb is not the question: a
        // POST binding only from the route no longer reads a body, so demanding a registration for it
        // would be advice about a call that is never made.
        foreach (var property in boundProperties)
        {
            if (property.Source == BindingSource.Body)
            {
                return bound.ToDisplayString();
            }
        }

        return null;
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

        // One class declared in several parts matches the syntax predicate once per part that
        // carries a base list (`partial class E : Endpoint<Q, R>` in one file, `partial class E :
        // IDisposable` in another — ordinary C#, and far more likely now that every endpoint has to
        // be partial). Collect() keeps every one of those analyses: value equality drives incremental
        // caching, not deduplication. Emitting them all would report each diagnostic once per part
        // and, worse, call AddSource twice with one hint name — which throws, aborts the generator
        // for the whole compilation (CS8785, a *warning* by default) and leaves every endpoint in the
        // assembly without its partial. Deduplicated here, after the pipeline's caching point, so no
        // non-value-equatable state enters the cache; ordered by name first so the survivor does not
        // depend on the order the syntax provider happened to visit the parts in.
        var distinctResults = results
            .OrderBy(static result => result.EndpointFullName, StringComparer.Ordinal)
            .GroupBy(static result => result.EndpointFullName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

        foreach (var result in distinctResults)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }

        var endpoints = distinctResults
            .Where(static r => r.Target is not null)
            .Select(static r => r.Target!.Value)
            .ToImmutableArray();

        if (endpoints.IsEmpty)
        {
            return;
        }

        var ns = rootNamespace;
        var ordered = endpoints.OrderBy(e => e.EndpointFullName, StringComparer.Ordinal).ToArray();

        // SYNE020 — must be reported before anything below is emitted: the metadata (and, for the
        // generated tiers, the binding) is emitted into the endpoint's own class, which requires it
        // (and every enclosing type) to be reopenable as partial. Reported for every kind, not only
        // the ones with a generated binding: SynapseEndpoint.CreateMetadata is abstract, so a
        // hand-bound or free-form endpoint needs its generated override just as much.
        foreach (var endpoint in ordered)
        {
            var location = endpoint.Location?.ToLocation() ?? Location.None;

            if (!endpoint.Declaration.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointDiagnostics.EndpointMustBePartial, location, endpoint.Declaration.TypeName));
            }

            // Reported at the enclosing type, not at the endpoint: the message names the enclosing
            // type, so a squiggle on the endpoint would point the author at the wrong declaration.
            foreach (var enclosing in endpoint.Declaration.NonPartialEnclosingTypes)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointDiagnostics.EndpointMustBePartial,
                    enclosing.Location?.ToLocation() ?? location,
                    enclosing.Name));
            }
        }

        // SYNE008 — reported once per distinct missing type, not once per endpoint, anchored at
        // whichever endpoint (in the same deterministic order used everywhere else in this method)
        // uses it first.
        ReportMissingJsonRegistrations(context, ordered, jsonContext);

        // Every endpoint gets a partial on itself, carrying its route metadata. Two endpoints binding
        // one message each resolve their own binding from their own route and verb, which is why
        // SYNE013 is gone — and why nothing is keyed by message type any more. Raw endpoints bind by
        // hand and have no BoundTypeFullName, so they get the metadata and nothing else.
        foreach (var endpoint in ordered)
        {
            var boundType = endpoint.Kind.HasGeneratedBinder()
                ? new BoundTypeInfo(endpoint.BoundTypeFullName, endpoint.BoundProperties,
                    endpoint.HasParameterlessConstructor, endpoint.PrimaryConstructorParameters)
                : null;

            context.AddSource(
                EndpointPartialEmitter.HintName(endpoint.Declaration),
                EndpointPartialEmitter.Emit(endpoint, boundType));
        }

        context.AddSource("SynapseEndpointGroup.g.cs", EndpointGroupEmitter.EmitGroup(ns, ordered));
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
    private static EquatableArray<JsonCallSite> CollectJsonCallSites(ClassDeclarationSyntax[] declarationParts,
        SemanticModel matchedModel)
    {
        List<JsonCallSite>? callSites = null;

        foreach (var classDeclaration in declarationParts)
        {
            // A part in another file needs its own semantic model; the matched part already has one,
            // and asking the compilation for a second model over the same tree would only pay for
            // binding it twice.
            var semanticModel = ReferenceEquals(classDeclaration.SyntaxTree, matchedModel.SyntaxTree)
                ? matchedModel
                : matchedModel.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);

            CollectJsonCallSites(classDeclaration, semanticModel, ref callSites);
        }

        return new EquatableArray<JsonCallSite>(callSites?.ToArray() ?? Array.Empty<JsonCallSite>());
    }

    /// <summary>
    ///     Appends the JSON-relevant call sites written inside one declaration part to
    ///     <paramref name="callSites" />, allocating the list only when there is something to add.
    /// </summary>
    private static void CollectJsonCallSites(ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        ref List<JsonCallSite>? callSites)
    {
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
