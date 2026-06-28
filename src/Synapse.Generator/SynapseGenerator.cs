using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     SynapseGenerator is responsible for generating source code at compile-time
///     as part of the incremental source generation process within the compilation
///     pipeline. It interacts with the Roslyn API and implements the IIncrementalGenerator
///     interface to enable efficient and reusable code generation.
/// </summary>
/// <remarks>
///     This generator is primarily used to process input syntax and semantic information
///     from the compilation, to generate source files dynamically.
/// </remarks>
[Generator]
public class SynapseGenerator : IIncrementalGenerator
{
    private const string BaseNamespace = "UnambitiousFx.Synapse";
    private const string AbstractionsNamespace = $"{BaseNamespace}.Abstractions";
    private const string ShortRequestHandlerAttributeName = "RequestHandler";
    private const string ShortEventHandlerAttributeName = "EventHandler";
    private const string ShortRequestStreamHandlerAttributeName = "StreamRequestHandler";
    private const string LongRequestHandlerAttributeName = $"{ShortRequestHandlerAttributeName}Attribute";
    private const string LongEventHandlerAttributeName = $"{ShortEventHandlerAttributeName}Attribute";
    private const string LongRequestStreamHandlerAttributeName = $"{ShortRequestStreamHandlerAttributeName}Attribute";
    private const string FullRequestHandlerAttributeName = $"{AbstractionsNamespace}.{LongRequestHandlerAttributeName}";
    private const string FullEventHandlerAttributeName = $"{AbstractionsNamespace}.{LongEventHandlerAttributeName}";
    private const string FullPipelineBehaviorAttributeName = $"{AbstractionsNamespace}.PipelineBehaviorAttribute";
    private const string FullValidatorAttributeName = $"{AbstractionsNamespace}.ValidatorAttribute";

    private const string FullRequestStreamHandlerAttributeName =
        $"{AbstractionsNamespace}.{LongRequestStreamHandlerAttributeName}";

    // Validator discovery metadata names.
    private const string RequestValidatorInterfaceName = $"{AbstractionsNamespace}.IRequestValidator";
    private const string RequestInterfaceName = $"{AbstractionsNamespace}.IRequest";

    // Pipeline interface metadata names (without arity suffix — appended when matching)
    private const string RequestPipelineBehaviorInterfaceName = $"{AbstractionsNamespace}.IRequestPipelineBehavior";
    private const string EventPipelineBehaviorInterfaceName = $"{AbstractionsNamespace}.IEventPipelineBehavior";

    private const string StreamRequestPipelineBehaviorInterfaceName =
        $"{AbstractionsNamespace}.IStreamRequestPipelineBehavior";

    /// <summary>
    ///     Display format that yields the fully-qualified name (with <c>global::</c> prefix and the full
    ///     enclosing-type chain) but omits generic type parameters — so nested types register correctly and
    ///     open-generic behaviors keep a base name the factory can close with type arguments.
    /// </summary>
    private static readonly SymbolDisplayFormat FullyQualifiedNoGenericsFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Included,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.None);

    /// <summary>
    ///     Renders a concrete response/item type ready for emission as a generic type argument. Uses Roslyn's
    ///     <see cref="SymbolDisplayFormat.FullyQualifiedFormat" />, which correctly globalizes every type shape
    ///     — including value tuples, pointers, function pointers, arrays and nullability — so the result needs
    ///     no further string munging (see known issue 012).
    /// </summary>
    private static string ToEmitName(ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string GetFullyQualifiedName(GeneratorAttributeSyntaxContext ctx, string @namespace, string className)
    {
        return ctx.TargetSymbol is INamedTypeSymbol symbol
            ? symbol.ToDisplayString(FullyQualifiedNoGenericsFormat)
            : $"{@namespace}.{className}";
    }

    /// <summary>
    ///     Initializes the SynapseGenerator by registering post-initialization output with the provided
    ///     <see cref="IncrementalGeneratorInitializationContext" />. This method is called during the
    ///     generator's setup phase to define the generator's behavior, such as adding generated source code.
    /// </summary>
    /// <param name="context">
    ///     The initialization context provided by the Roslyn API. It provides methods and registration points
    ///     that allow the generator to specify how it interacts with the compilation process.
    /// </param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get the compilation
        var compilationProvider = context.CompilationProvider;

        // Transform the compilation to extract the root namespace
        var rootNamespaceProvider = compilationProvider
            .Select(static (compilation,
                _) => compilation.GetRootNamespaceFromAssemblyAttributes());


        var requestHandlerWithResponseDetails = context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FullRequestHandlerAttributeName}`2", static (node,
                _) =>
            {
                var isClass = node is ClassDeclarationSyntax;

                return isClass;
            },
            static (ctx,
                _) => GetRequestHandlerDetail(ctx));
        var requestHandlerWithoutResponseDetails = context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FullRequestHandlerAttributeName}`1", static (node,
                _) =>
            {
                var isClass = node is ClassDeclarationSyntax;

                return isClass;
            },
            static (ctx,
                _) => GetRequestHandlerDetail(ctx));
        var eventHandlerDetails = context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FullEventHandlerAttributeName}`1", static (node,
                _) =>
            {
                var isClass = node is ClassDeclarationSyntax;

                return isClass;
            },
            static (ctx,
                _) => GetEventHandlerDetail(ctx));

        var streamRequestDetails = context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FullRequestStreamHandlerAttributeName}`2", static (node,
                _) =>
            {
                var isClass = node is ClassDeclarationSyntax;

                return isClass;
            },
            static (ctx,
                _) => GetStreamRequestHandlerDetail(ctx));

        var allHandlerDetails = requestHandlerWithResponseDetails.Collect()
            .Combine(requestHandlerWithoutResponseDetails.Collect())
            .Select(static (tuple,
                _) => tuple.Left.AddRange(tuple.Right))
            .Combine(eventHandlerDetails.Collect())
            .Select(static (tuple,
                _) => tuple.Left.AddRange(tuple.Right))
            .Combine(streamRequestDetails.Collect())
            .Select(static (tuple,
                _) => tuple.Left.AddRange(tuple.Right));

        // Collect [PipelineBehavior]-decorated classes
        var behaviorDetails = context.SyntaxProvider.ForAttributeWithMetadataName(
            FullPipelineBehaviorAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => GetBehaviorDetail(ctx))
            .Collect();

        // Collect [Validator]-decorated classes
        var validatorDetails = context.SyntaxProvider.ForAttributeWithMetadataName(
            FullValidatorAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => GetValidatorDetail(ctx))
            .Collect();

        // Detect the assembly-level opt-in for CQRS boundary enforcement. A runtime fluent call is
        // invisible to the generator, so enforcement is expressed as an assembly attribute the generator
        // can see and turn into closed (AOT-safe) registrations.
        var cqrsEnabledProvider = compilationProvider
            .Select(static (compilation, _) => compilation.IsCqrsBoundaryEnforcementEnabled());

        // Discover every request/event/stream handler target visible in the compilation — including
        // those declared in referenced assemblies — so an assembly's open-generic behaviors can be
        // cross-producted against the whole reference graph (closed, AOT-safe registrations), not just
        // same-assembly handlers. Like ExtractEventInfo, this rides on the compilation and therefore
        // re-runs on every edit; that matches the existing event-dispatcher tradeoff.
        var behaviorTargetsProvider = compilationProvider
            .Select(static (compilation, _) => ExtractAllBehaviorTargets(compilation));

        var crossAssemblyDisabledProvider = compilationProvider
            .Select(static (compilation, _) => compilation.IsCrossAssemblyBehaviorsDisabled());

        // Extract event info here so it can be folded into the RegisterGroup output (RegisterGroup
        // now also implements IEventDispatcherRegistration — no separate file is generated).
        var eventInfoProvider = compilationProvider
            .Select(static (compilation, _) => ExtractEventInfo(compilation));

        var combinedProvider = allHandlerDetails
            .Combine(rootNamespaceProvider)
            .Combine(behaviorDetails)
            .Combine(cqrsEnabledProvider)
            .Combine(behaviorTargetsProvider)
            .Combine(crossAssemblyDisabledProvider)
            .Combine(validatorDetails)
            .Combine(eventInfoProvider);

        context.RegisterSourceOutput(combinedProvider, static (ctx, tuple) =>
        {
            var (((((((details, rootNamespace), behaviors), cqrsEnabled), behaviorTargets), crossAssemblyDisabled),
                validatorScans), eventInfo) = tuple;

            ctx.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "MDG005",
                    "RegisterGroup generation started",
                    "RegisterGroup generation started with {0} handlers and root namespace {1}",
                    "Synapse.Generator",
                    DiagnosticSeverity.Info,
                    true),
                Location.None,
                details.Length, rootNamespace));

            if (string.IsNullOrEmpty(rootNamespace))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "MDG001",
                        "Root namespace not found",
                        "Root namespace could not be determined. Please ensure assembly has a root namespace defined.",
                        "Synapse.Generator",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
                return;
            }

            if (details.Length == 0)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "MDG002",
                        "No handler found",
                        "No handler found in this assembly. Use RequestHandlerAttribute or EventHandlerAttribute to mark a class as a handler.",
                        "Synapse.Generator",
                        DiagnosticSeverity.Info,
                        true),
                    Location.None));
            }
            else
            {
                foreach (var detail in details)
                {
                    if (detail is null)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "MDG004",
                                "Null handler found",
                                "Null handler found in this assembly. Use RequestHandlerAttribute or EventHandlerAttribute to mark a class as a handler.",
                                "Synapse.Generator",
                                DiagnosticSeverity.Warning,
                                true),
                            Location.None));
                    }
                    else
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "MDG003",
                                "Handler found",
                                $"Handler {detail.Value.ClassName}",
                                "Synapse.Generator",
                                DiagnosticSeverity.Info,
                                true),
                            detail.Value.Location?.ToLocation() ?? Location.None));
                    }
                }
            }

            // Validate behaviors — warn if a [PipelineBehavior] class implements no pipeline interface,
            // and flatten the per-class scans into the list of behaviors to emit. A single class can
            // implement several pipeline interfaces, so each scan may contribute multiple behaviors.
            var behaviorList = ImmutableArray.CreateBuilder<BehaviorDetail>();
            foreach (var scan in behaviors)
            {
                if (scan is null)
                {
                    continue;
                }

                if (scan.Value.Behaviors.Count == 0)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "MDG008",
                            "Invalid pipeline behavior",
                            "[PipelineBehavior] is applied to a class that does not implement any known pipeline interface. Ensure the class implements IRequestPipelineBehavior<>, IEventPipelineBehavior<>, or IStreamRequestPipelineBehavior<,>.",
                            "Synapse.Generator",
                            DiagnosticSeverity.Warning,
                            true),
                        scan.Value.Location?.ToLocation() ?? Location.None));
                    continue;
                }

                foreach (var behavior in scan.Value.Behaviors)
                {
                    // A class type parameter not bound by the implemented interface cannot be inferred when the
                    // behavior is closed over a handler. Emitting a registration anyway would produce code that
                    // fails to compile (CS0305), so report a clear error and skip this behavior instead.
                    if (behavior.HasUnbindableTypeParameter)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "MDG010",
                                "Open-generic behavior has uninferable type parameter",
                                $"Open-generic behavior '{behavior.ClassName}' has a type parameter that is not bound by the pipeline interface it implements, so it cannot be closed over a handler. Remove the extra type parameter or bind it through the interface.",
                                "Synapse.Generator",
                                DiagnosticSeverity.Error,
                                true),
                            scan.Value.Location?.ToLocation() ?? Location.None));
                        continue;
                    }

                    behaviorList.Add(behavior);
                }
            }

            // Validate validators — warn if a [Validator] class implements no IRequestValidator<> interface,
            // and collect the rest for emission.
            var validatorList = ImmutableArray.CreateBuilder<ValidatorDetail>();
            foreach (var scan in validatorScans)
            {
                if (scan is null)
                {
                    continue;
                }

                if (scan.Value.Detail is not { } validator)
                {
                    if (scan.Value.AmbiguousResponseRequest is { } ambiguousRequest)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "MDG011",
                                "Ambiguous validator response type",
                                $"Request '{ambiguousRequest}' implements multiple IRequest<TResponse>; the validator response type cannot be determined unambiguously. Implement a single IRequest<TResponse>.",
                                "Synapse.Generator",
                                DiagnosticSeverity.Error,
                                true),
                            scan.Value.Location?.ToLocation() ?? Location.None));
                        continue;
                    }

                    ctx.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "MDG009",
                            "Invalid validator",
                            "[Validator] is applied to a class that does not implement IRequestValidator<>. Ensure the class implements IRequestValidator<TRequest>.",
                            "Synapse.Generator",
                            DiagnosticSeverity.Warning,
                            true),
                        scan.Value.Location?.ToLocation() ?? Location.None));
                    continue;
                }

                validatorList.Add(validator);
            }

            // Report event-type diagnostics alongside the RegisterGroup output (dispatcher registration
            // is now folded into RegisterGroup; no separate file is generated).
            if (eventInfo.EventTypes.Length == 0)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "MDG006",
                        "No event types found",
                        "No IEvent implementations found in this assembly. Event dispatcher registrations will not be generated.",
                        "Synapse.Generator",
                        DiagnosticSeverity.Info,
                        true),
                    Location.None));
            }
            else
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "MDG007",
                        "Event dispatcher registration included in RegisterGroup",
                        "Generating event dispatcher registrations for {0} event types with {1} handlers (included in RegisterGroup.g.cs)",
                        "Synapse.Generator",
                        DiagnosticSeverity.Info,
                        true),
                    Location.None,
                    eventInfo.EventTypes.Length,
                    eventInfo.HandlerTypes.Length));
            }

            ctx.AddSource("RegisterGroup.g.cs",
                RegisterGroupFactory.Create(rootNamespace, AbstractionsNamespace, details,
                    behaviorList.ToImmutable(), cqrsEnabled, behaviorTargets, crossAssemblyDisabled,
                    validatorList.ToImmutable(), eventInfo));
        });
    }

    private static HandlerDetail? GetStreamRequestHandlerDetail(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attribute in ctx.Attributes)
        {
            if (!(attribute.AttributeClass?.Name is LongRequestStreamHandlerAttributeName
                    or ShortRequestStreamHandlerAttributeName))
                // wrong attribute
            {
                continue;
            }

            if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
                // not a class
            {
                continue;
            }

            var className = classDeclaration.Identifier.ValueText;
            var @namespace = classDeclaration.GetNamespace();
            var (requestType, responseType, requestSatisfying, responseSatisfying) = GetRequestInfo(attribute);
            var location = LocationInfo.CreateFrom(classDeclaration.GetLocation());
            var handlerType = HandlerType.StreamRequestHandler;
            var fullyQualifiedName = GetFullyQualifiedName(ctx, @namespace, className);
            return new HandlerDetail(handlerType, className, @namespace, fullyQualifiedName, requestType, responseType,
                location, requestSatisfying, responseSatisfying);
        }

        return null;
    }

    private static HandlerDetail? GetRequestHandlerDetail(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attribute in ctx.Attributes)
        {
            if (!(attribute.AttributeClass?.Name is LongRequestHandlerAttributeName
                    or ShortRequestHandlerAttributeName))
                // wrong attribute
            {
                continue;
            }

            if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
                // not a class
            {
                continue;
            }

            var className = classDeclaration.Identifier.ValueText;
            var @namespace = classDeclaration.GetNamespace();
            var (requestType, responseType, requestSatisfying, responseSatisfying) = GetRequestInfo(attribute);


            var location = LocationInfo.CreateFrom(classDeclaration.GetLocation());
            var handlerType = HandlerType.RequestHandler;
            var fullyQualifiedName = GetFullyQualifiedName(ctx, @namespace, className);
            return new HandlerDetail(handlerType, className, @namespace, fullyQualifiedName, requestType, responseType,
                location, requestSatisfying, responseSatisfying);
        }

        return null;
    }

    private static HandlerDetail? GetEventHandlerDetail(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attribute in ctx.Attributes)
        {
            if (!(attribute.AttributeClass?.Name is LongEventHandlerAttributeName or ShortEventHandlerAttributeName))
                // wrong attribute
            {
                continue;
            }

            if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
                // not a class
            {
                continue;
            }

            var className = classDeclaration.Identifier.ValueText;
            var @namespace = classDeclaration.GetNamespace();
            var (requestType, responseType, requestSatisfying, responseSatisfying) = GetRequestInfo(attribute);

            var location = LocationInfo.CreateFrom(classDeclaration.GetLocation());

            var fullyQualifiedName = GetFullyQualifiedName(ctx, @namespace, className);
            return new HandlerDetail(HandlerType.EventHandler, className, @namespace, fullyQualifiedName, requestType,
                responseType, location, requestSatisfying, responseSatisfying);
        }

        return null;
    }

    private static BehaviorScan? GetBehaviorDetail(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        var isOpenGeneric = classSymbol.IsGenericType;
        var className = classDeclaration.Identifier.ValueText;
        var @namespace = classDeclaration.GetNamespace();
        var fullyQualifiedName = classSymbol.ToDisplayString(FullyQualifiedNoGenericsFormat);
        var location = LocationInfo.CreateFrom(classDeclaration.GetLocation());

        // A class may implement more than one pipeline interface — emit a behavior for each, rather
        // than arbitrarily picking the first one found in AllInterfaces.
        var behaviors = new List<BehaviorDetail>();
        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var ifaceNamespace = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var ifaceFullName = $"{ifaceNamespace}.{iface.MetadataName}";

            if (ifaceFullName == $"{RequestPipelineBehaviorInterfaceName}`1" && iface.TypeArguments.Length == 1)
            {
                var requestType = isOpenGeneric
                    ? iface.TypeArguments[0].Name
                    : ToEmitName(iface.TypeArguments[0]);
                behaviors.Add(new BehaviorDetail(className, @namespace, fullyQualifiedName, BehaviorKind.Request,
                    isOpenGeneric, requestType, null, GetConstraintNames(iface.TypeArguments[0]), default,
                    BuildClosingTypeArgumentMap(classSymbol, iface, isOpenGeneric)));
            }
            else if (ifaceFullName == $"{RequestPipelineBehaviorInterfaceName}`2" && iface.TypeArguments.Length == 2)
            {
                var requestType = isOpenGeneric
                    ? iface.TypeArguments[0].Name
                    : ToEmitName(iface.TypeArguments[0]);
                var responseType = isOpenGeneric
                    ? iface.TypeArguments[1].Name
                    : ToEmitName(iface.TypeArguments[1]);
                behaviors.Add(new BehaviorDetail(className, @namespace, fullyQualifiedName,
                    BehaviorKind.RequestWithResponse, isOpenGeneric, requestType, responseType,
                    GetConstraintNames(iface.TypeArguments[0]), GetConstraintNames(iface.TypeArguments[1]),
                    BuildClosingTypeArgumentMap(classSymbol, iface, isOpenGeneric)));
            }
            else if (ifaceFullName == $"{EventPipelineBehaviorInterfaceName}`1" && iface.TypeArguments.Length == 1)
            {
                var eventType = isOpenGeneric
                    ? iface.TypeArguments[0].Name
                    : ToEmitName(iface.TypeArguments[0]);
                behaviors.Add(new BehaviorDetail(className, @namespace, fullyQualifiedName, BehaviorKind.Event,
                    isOpenGeneric, eventType, null, GetConstraintNames(iface.TypeArguments[0]), default,
                    BuildClosingTypeArgumentMap(classSymbol, iface, isOpenGeneric)));
            }
            else if (ifaceFullName == $"{StreamRequestPipelineBehaviorInterfaceName}`2" &&
                     iface.TypeArguments.Length == 2)
            {
                var requestType = isOpenGeneric
                    ? iface.TypeArguments[0].Name
                    : ToEmitName(iface.TypeArguments[0]);
                var itemType = isOpenGeneric
                    ? iface.TypeArguments[1].Name
                    : ToEmitName(iface.TypeArguments[1]);
                behaviors.Add(new BehaviorDetail(className, @namespace, fullyQualifiedName, BehaviorKind.StreamRequest,
                    isOpenGeneric, requestType, itemType, GetConstraintNames(iface.TypeArguments[0]),
                    GetConstraintNames(iface.TypeArguments[1]),
                    BuildClosingTypeArgumentMap(classSymbol, iface, isOpenGeneric)));
            }
        }

        // An empty Behaviors collection signals MDG008 (no known pipeline interface implemented).
        return new BehaviorScan(location, EquatableArray<BehaviorDetail>.From(behaviors));
    }

    private static ValidatorScan? GetValidatorDetail(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        var className = classDeclaration.Identifier.ValueText;
        var @namespace = classDeclaration.GetNamespace();
        var fullyQualifiedName = classSymbol.ToDisplayString(FullyQualifiedNoGenericsFormat);
        var location = LocationInfo.CreateFrom(classDeclaration.GetLocation());

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType || iface.TypeArguments.Length != 1)
            {
                continue;
            }

            var ifaceNamespace = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var ifaceFullName = $"{ifaceNamespace}.{iface.MetadataName}";

            if (ifaceFullName != $"{RequestValidatorInterfaceName}`1")
            {
                continue;
            }

            var requestSymbol = iface.TypeArguments[0];
            var requestType = ToEmitName(requestSymbol);
            var (responseType, ambiguous) = GetRequestResponseType(requestSymbol);
            if (ambiguous)
            {
                // Request implements multiple IRequest<TResponse>; the response binding is ambiguous —
                // signal MDG011 (null detail + request name) rather than guessing a response type.
                // Use the plain display name for the human-readable diagnostic message, not the
                // global::-prefixed emit name.
                return new ValidatorScan(location, null, requestSymbol.ToDisplayString());
            }

            return new ValidatorScan(location,
                new ValidatorDetail(className, @namespace, fullyQualifiedName, requestType, responseType));
        }

        // No IRequestValidator<> implemented — signal MDG009.
        return new ValidatorScan(location, null);
    }

    /// <summary>
    ///     Returns the fully-qualified response type of a request that implements
    ///     <c>IRequest&lt;TResponse&gt;</c>, or null when the request only implements the no-response
    ///     <c>IRequest</c> marker. <c>Ambiguous</c> is true when the request implements more than one
    ///     <c>IRequest&lt;TResponse&gt;</c> with distinct response types, in which case the response cannot be
    ///     bound deterministically and the caller should report MDG011 instead of emitting a registration.
    /// </summary>
    private static (string? ResponseType, bool Ambiguous) GetRequestResponseType(ITypeSymbol requestSymbol)
    {
        string? found = null;
        var ambiguous = false;
        foreach (var iface in requestSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType || iface.TypeArguments.Length != 1)
            {
                continue;
            }

            var ifaceNamespace = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if ($"{ifaceNamespace}.{iface.MetadataName}" != $"{RequestInterfaceName}`1")
            {
                continue;
            }

            // Dedupe by emit name: the same response type seen twice (e.g. via interface inheritance)
            // is not ambiguous; only distinct response types are.
            var candidate = ToEmitName(iface.TypeArguments[0]);
            if (found is null)
            {
                found = candidate;
            }
            else if (found != candidate)
            {
                ambiguous = true;
            }
        }

        return (found, ambiguous);
    }

    /// <summary>
    ///     Maps each of the behavior class's type parameters (in declaration order) to the index of the
    ///     implemented interface's type argument that binds it: <c>0</c> for the request/event slot, <c>1</c>
    ///     for the response/item slot, or <c>-1</c> when no interface type argument references the parameter
    ///     (it cannot be inferred when the class is closed). This drives the arity and order of type arguments
    ///     emitted at close time, fixing classes whose own generic arity or parameter order differs from the
    ///     interface they implement. Returns an empty array for closed (non-generic) behaviors.
    /// </summary>
    private static EquatableArray<int> BuildClosingTypeArgumentMap(INamedTypeSymbol classSymbol,
        INamedTypeSymbol iface,
        bool isOpenGeneric)
    {
        if (!isOpenGeneric || classSymbol.TypeParameters.Length == 0)
        {
            return default;
        }

        var map = new int[classSymbol.TypeParameters.Length];
        for (var i = 0; i < map.Length; i++)
        {
            map[i] = -1;
        }

        for (var argIndex = 0; argIndex < iface.TypeArguments.Length; argIndex++)
        {
            if (iface.TypeArguments[argIndex] is ITypeParameterSymbol
                {
                    TypeParameterKind: TypeParameterKind.Type
                } typeParameter
                && SymbolEqualityComparer.Default.Equals(typeParameter.ContainingType, classSymbol)
                && typeParameter.Ordinal < map.Length)
            {
                map[typeParameter.Ordinal] = argIndex;
            }
        }

        return EquatableArray<int>.From(map);
    }

    /// <summary>
    ///     Returns the named-type constraints on an open-generic behavior's type parameter as fully-qualified
    ///     display strings. Closed type arguments (and parameters with no type constraint) yield an empty array.
    /// </summary>
    private static EquatableArray<string> GetConstraintNames(ITypeSymbol typeArgument)
    {
        if (typeArgument is not ITypeParameterSymbol { ConstraintTypes.Length: > 0 } typeParameter)
        {
            return default;
        }

        // Only keep concrete named-type constraints (e.g. ICommand). Constraints that reference the
        // behavior's own type parameters — such as `where TRequest : IRequest<TResponse>` — are satisfied
        // by construction when the behavior is closed over a matching handler, so filtering on them would
        // wrongly exclude every handler.
        var names = typeParameter.ConstraintTypes
            .Where(c => !ContainsTypeParameter(c))
            .Select(c => c.ToDisplayString())
            .ToList();

        return names.Count > 0 ? EquatableArray<string>.From(names) : default;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return true;
            case IArrayTypeSymbol array:
                return ContainsTypeParameter(array.ElementType);
            case INamedTypeSymbol named:
                foreach (var typeArgument in named.TypeArguments)
                {
                    if (ContainsTypeParameter(typeArgument))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static (string RequestType, string? ResponseType, EquatableArray<string> RequestSatisfying,
        EquatableArray<string> ResponseSatisfying) GetRequestInfo(AttributeData attribute)
    {
        // Get the attribute constructor's type arguments
        var typeArgs = attribute.AttributeClass?.TypeArguments;
        if (typeArgs is null ||
            typeArgs.Value.Length == 0)
        {
            return (string.Empty, null, default, default);
        }


        // Get the fully qualified name of the request type
        var requestSymbol = typeArgs.Value[0];
        var requestType = ToEmitName(requestSymbol);
        var requestSatisfying = GetSatisfyingTypeNames(requestSymbol);

        // Check if there's a response type (generic attribute with 2 type parameters)
        string? responseType = null;
        var responseSatisfying = default(EquatableArray<string>);
        if (typeArgs.Value.Length > 1)
        {
            var responseSymbol = typeArgs.Value[1];
            responseType = ToEmitName(responseSymbol);
            responseSatisfying = GetSatisfyingTypeNames(responseSymbol);
        }

        return (requestType, responseType, requestSatisfying, responseSatisfying);
    }

    /// <summary>
    ///     Returns the type itself plus all of its base types and implemented interfaces as fully-qualified
    ///     display strings — the set a constraint type must be a member of for the type to satisfy it.
    /// </summary>
    private static EquatableArray<string> GetSatisfyingTypeNames(ITypeSymbol? type)
    {
        if (type is null)
        {
            return default;
        }

        var names = new List<string> { type.ToDisplayString() };
        foreach (var iface in type.AllInterfaces)
        {
            names.Add(iface.ToDisplayString());
        }

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            names.Add(baseType.ToDisplayString());
        }

        return EquatableArray<string>.From(names);
    }

    /// <summary>
    ///     Extracts all types that implement IEvent and IEventHandler from the compilation.
    ///     This is used to generate dispatcher registrations for NativeAOT compatibility.
    /// </summary>
    private static EventInfo ExtractEventInfo(Compilation compilation)
    {
        var eventTypes = new HashSet<string>();
        var handlerTypes = new HashSet<string>();

        var iEventSymbol = compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IEvent");
        var iEventHandlerSymbol = compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IEventHandler`1");

        if (iEventSymbol == null)
        {
            return new EventInfo(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
        }

        // Iterate through all named types in the compilation
        var visitor = new EventInfoSymbolVisitor(iEventSymbol, iEventHandlerSymbol, eventTypes, handlerTypes);
        visitor.Visit(compilation.GlobalNamespace);

        return new EventInfo(eventTypes.ToImmutableArray(), handlerTypes.ToImmutableArray());
    }

    /// <summary>
    ///     Collects every request/event/stream handler target reachable in the compilation — own assembly and
    ///     referenced assemblies alike — as <see cref="HandlerDetail" />s for open-generic behavior cross-product.
    ///     Only the request/response types and their satisfying-type sets are populated; the handler class name
    ///     and location are irrelevant here (the handler itself is registered by its own assembly's RegisterGroup).
    ///     Results are deduplicated by (kind, request type, response type).
    /// </summary>
    private static ImmutableArray<HandlerDetail> ExtractAllBehaviorTargets(Compilation compilation)
    {
        var requestHandler1 = compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IRequestHandler`1");
        var requestHandler2 = compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IRequestHandler`2");
        var eventHandler1 = compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IEventHandler`1");
        var streamHandler2 = compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IStreamRequestHandler`2");

        if (requestHandler1 is null && requestHandler2 is null && eventHandler1 is null && streamHandler2 is null)
        {
            return ImmutableArray<HandlerDetail>.Empty;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var targets = ImmutableArray.CreateBuilder<HandlerDetail>();
        var visitor = new BehaviorTargetSymbolVisitor(requestHandler1, requestHandler2, eventHandler1,
            streamHandler2, seen, targets);
        visitor.Visit(compilation.GlobalNamespace);

        return targets.ToImmutable();
    }

    /// <summary>
    ///     Symbol visitor that finds all concrete types implementing a Synapse handler interface and records
    ///     the corresponding behavior target (request/event/stream type + response/item type).
    /// </summary>
    private class BehaviorTargetSymbolVisitor : SymbolVisitor
    {
        private readonly INamedTypeSymbol? _eventHandler1;
        private readonly INamedTypeSymbol? _requestHandler1;
        private readonly INamedTypeSymbol? _requestHandler2;
        private readonly HashSet<string> _seen;
        private readonly INamedTypeSymbol? _streamHandler2;
        private readonly ImmutableArray<HandlerDetail>.Builder _targets;

        public BehaviorTargetSymbolVisitor(
            INamedTypeSymbol? requestHandler1,
            INamedTypeSymbol? requestHandler2,
            INamedTypeSymbol? eventHandler1,
            INamedTypeSymbol? streamHandler2,
            HashSet<string> seen,
            ImmutableArray<HandlerDetail>.Builder targets)
        {
            _requestHandler1 = requestHandler1;
            _requestHandler2 = requestHandler2;
            _eventHandler1 = eventHandler1;
            _streamHandler2 = streamHandler2;
            _seen = seen;
            _targets = targets;
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                member.Accept(this);
            }
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            if (!symbol.IsAbstract && symbol.TypeKind != TypeKind.Interface)
            {
                foreach (var iface in symbol.AllInterfaces)
                {
                    if (!iface.IsGenericType)
                    {
                        continue;
                    }

                    var definition = iface.ConstructedFrom;
                    if (Matches(definition, _requestHandler2) && iface.TypeArguments.Length == 2)
                    {
                        Add(HandlerType.RequestHandler, iface.TypeArguments[0], iface.TypeArguments[1]);
                    }
                    else if (Matches(definition, _requestHandler1) && iface.TypeArguments.Length == 1)
                    {
                        Add(HandlerType.RequestHandler, iface.TypeArguments[0], null);
                    }
                    else if (Matches(definition, _eventHandler1) && iface.TypeArguments.Length == 1)
                    {
                        Add(HandlerType.EventHandler, iface.TypeArguments[0], null);
                    }
                    else if (Matches(definition, _streamHandler2) && iface.TypeArguments.Length == 2)
                    {
                        Add(HandlerType.StreamRequestHandler, iface.TypeArguments[0], iface.TypeArguments[1]);
                    }
                }
            }

            foreach (var nestedType in symbol.GetTypeMembers())
            {
                nestedType.Accept(this);
            }
        }

        private static bool Matches(INamedTypeSymbol definition, INamedTypeSymbol? target)
        {
            return target is not null && SymbolEqualityComparer.Default.Equals(definition, target);
        }

        private void Add(HandlerType handlerType, ITypeSymbol target, ITypeSymbol? response)
        {
            // Skip open-generic handler definitions (e.g. an adapter implementing
            // IRequestHandler<TRequest, TResponse>): their type arguments are unbound type parameters,
            // not concrete request types, and cannot be closed over.
            if (ContainsTypeParameter(target) || (response is not null && ContainsTypeParameter(response)))
            {
                return;
            }

            var targetType = ToEmitName(target);
            var responseType = response is null ? null : ToEmitName(response);
            var key = $"{handlerType}|{targetType}|{responseType}";
            if (!_seen.Add(key))
            {
                return;
            }

            _targets.Add(new HandlerDetail(handlerType, string.Empty, string.Empty, string.Empty, targetType,
                responseType, null, GetSatisfyingTypeNames(target), GetSatisfyingTypeNames(response)));
        }
    }

    /// <summary>
    ///     Symbol visitor that finds all types implementing IEvent and IEventHandler.
    /// </summary>
    private class EventInfoSymbolVisitor : SymbolVisitor
    {
        private readonly HashSet<string> _eventTypes;
        private readonly HashSet<string> _handlerTypes;
        private readonly INamedTypeSymbol? _iEventHandlerSymbol;
        private readonly INamedTypeSymbol _iEventSymbol;

        public EventInfoSymbolVisitor(
            INamedTypeSymbol iEventSymbol,
            INamedTypeSymbol? iEventHandlerSymbol,
            HashSet<string> eventTypes,
            HashSet<string> handlerTypes)
        {
            _iEventSymbol = iEventSymbol;
            _iEventHandlerSymbol = iEventHandlerSymbol;
            _eventTypes = eventTypes;
            _handlerTypes = handlerTypes;
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                member.Accept(this);
            }
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            // Check if this type implements IEvent
            if (ImplementsIEvent(symbol))
            {
                _eventTypes.Add(symbol.ToDisplayString());
            }

            // Check if this type implements IEventHandler<T>
            if (_iEventHandlerSymbol != null && ImplementsIEventHandler(symbol))
            {
                _handlerTypes.Add(symbol.ToDisplayString());
            }

            // Visit nested types
            foreach (var nestedType in symbol.GetTypeMembers())
            {
                nestedType.Accept(this);
            }
        }

        private bool ImplementsIEvent(INamedTypeSymbol typeSymbol)
        {
            // Skip abstract types and interfaces
            if (typeSymbol.IsAbstract || typeSymbol.TypeKind == TypeKind.Interface)
            {
                return false;
            }

            // Check all interfaces
            foreach (var @interface in typeSymbol.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(@interface, _iEventSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ImplementsIEventHandler(INamedTypeSymbol typeSymbol)
        {
            // Skip abstract types and interfaces
            if (typeSymbol.IsAbstract || typeSymbol.TypeKind == TypeKind.Interface)
            {
                return false;
            }

            // Check all interfaces
            foreach (var @interface in typeSymbol.AllInterfaces)
            {
                if (@interface.IsGenericType &&
                    SymbolEqualityComparer.Default.Equals(@interface.ConstructedFrom, _iEventHandlerSymbol))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
