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

    private const string FullRequestStreamHandlerAttributeName =
        $"{AbstractionsNamespace}.{LongRequestStreamHandlerAttributeName}";

    // Pipeline interface metadata names (without arity suffix — appended when matching)
    private const string RequestPipelineBehaviorInterfaceName = $"{AbstractionsNamespace}.IRequestPipelineBehavior";
    private const string EventPipelineBehaviorInterfaceName = $"{AbstractionsNamespace}.IEventPipelineBehavior";

    private const string StreamRequestPipelineBehaviorInterfaceName =
        $"{AbstractionsNamespace}.IStreamRequestPipelineBehavior";

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

        var combinedProvider = allHandlerDetails
            .Combine(rootNamespaceProvider)
            .Combine(behaviorDetails);

        context.RegisterSourceOutput(combinedProvider, static (ctx, tuple) =>
        {
            var ((details, rootNamespace), behaviors) = tuple;

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
                    behaviorList.Add(behavior);
                }
            }

            ctx.AddSource("RegisterGroup.g.cs",
                RegisterGroupFactory.Create(rootNamespace, AbstractionsNamespace, details, behaviorList.ToImmutable()));
        });

        // Generate event dispatcher registrations for NativeAOT support
        var eventInfoProvider = compilationProvider
            .Select(static (compilation, _) => ExtractEventInfo(compilation));

        var eventDispatcherProvider = eventInfoProvider.Combine(rootNamespaceProvider);

        context.RegisterSourceOutput(eventDispatcherProvider, static (ctx, tuple) =>
        {
            var (eventInfo, rootNamespace) = tuple;

            if (string.IsNullOrEmpty(rootNamespace))
            {
                return;
            }

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
                return;
            }

            ctx.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "MDG007",
                    "Event dispatcher generation started",
                    "Generating event dispatcher registrations for {0} event types with {1} handlers",
                    "Synapse.Generator",
                    DiagnosticSeverity.Info,
                    true),
                Location.None,
                eventInfo.EventTypes.Length,
                eventInfo.HandlerTypes.Length));

            ctx.AddSource("EventDispatcherRegistration.g.cs",
                EventDispatcherRegistrationFactory.Create(rootNamespace, AbstractionsNamespace, eventInfo));
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
            return new HandlerDetail(handlerType, className, @namespace, requestType, responseType, location,
                requestSatisfying, responseSatisfying);
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
            return new HandlerDetail(handlerType, className, @namespace, requestType, responseType, location,
                requestSatisfying, responseSatisfying);
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

            return new HandlerDetail(HandlerType.EventHandler, className, @namespace, requestType, responseType,
                location, requestSatisfying, responseSatisfying);
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

        // Read Order from attribute
        var order = 0;
        foreach (var attr in ctx.Attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Order" && named.Value.Value is int o)
                {
                    order = o;
                }
            }
        }

        var isOpenGeneric = classSymbol.IsGenericType;
        var className = classDeclaration.Identifier.ValueText;
        var @namespace = classDeclaration.GetNamespace();
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
                    : iface.TypeArguments[0].ToDisplayString();
                behaviors.Add(new BehaviorDetail(className, @namespace, BehaviorKind.Request, isOpenGeneric,
                    requestType, null, order, GetConstraintNames(iface.TypeArguments[0]), default));
            }
            else if (ifaceFullName == $"{RequestPipelineBehaviorInterfaceName}`2" && iface.TypeArguments.Length == 2)
            {
                var requestType = isOpenGeneric
                    ? iface.TypeArguments[0].Name
                    : iface.TypeArguments[0].ToDisplayString();
                var responseType = isOpenGeneric
                    ? iface.TypeArguments[1].Name
                    : iface.TypeArguments[1].ToDisplayString();
                behaviors.Add(new BehaviorDetail(className, @namespace, BehaviorKind.RequestWithResponse, isOpenGeneric,
                    requestType, responseType, order, GetConstraintNames(iface.TypeArguments[0]),
                    GetConstraintNames(iface.TypeArguments[1])));
            }
            else if (ifaceFullName == $"{EventPipelineBehaviorInterfaceName}`1" && iface.TypeArguments.Length == 1)
            {
                var eventType = isOpenGeneric
                    ? iface.TypeArguments[0].Name
                    : iface.TypeArguments[0].ToDisplayString();
                behaviors.Add(new BehaviorDetail(className, @namespace, BehaviorKind.Event, isOpenGeneric,
                    eventType, null, order, GetConstraintNames(iface.TypeArguments[0]), default));
            }
            else if (ifaceFullName == $"{StreamRequestPipelineBehaviorInterfaceName}`2" &&
                     iface.TypeArguments.Length == 2)
            {
                var requestType = isOpenGeneric
                    ? iface.TypeArguments[0].Name
                    : iface.TypeArguments[0].ToDisplayString();
                var itemType = isOpenGeneric
                    ? iface.TypeArguments[1].Name
                    : iface.TypeArguments[1].ToDisplayString();
                behaviors.Add(new BehaviorDetail(className, @namespace, BehaviorKind.StreamRequest, isOpenGeneric,
                    requestType, itemType, order, GetConstraintNames(iface.TypeArguments[0]),
                    GetConstraintNames(iface.TypeArguments[1])));
            }
        }

        // An empty Behaviors collection signals MDG008 (no known pipeline interface implemented).
        return new BehaviorScan(location, EquatableArray<BehaviorDetail>.From(behaviors));
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
        var requestType = requestSymbol.ToDisplayString();
        var requestSatisfying = GetSatisfyingTypeNames(requestSymbol);

        // Check if there's a response type (generic attribute with 2 type parameters)
        string? responseType = null;
        var responseSatisfying = default(EquatableArray<string>);
        if (typeArgs.Value.Length > 1)
        {
            var responseSymbol = typeArgs.Value[1];
            responseType = responseSymbol.ToDisplayString();
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
