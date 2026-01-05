using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     MediatorGenerator is responsible for generating source code at compile-time
///     as part of the incremental source generation process within the compilation
///     pipeline. It interacts with the Roslyn API and implements the IIncrementalGenerator
///     interface to enable efficient and reusable code generation.
/// </summary>
/// <remarks>
///     This generator is primarily used to process input syntax and semantic information
///     from the compilation, to generate source files dynamically.
/// </remarks>
[Generator]
public class MediatorGenerator : IIncrementalGenerator
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

    private const string FullRequestStreamHandlerAttributeName =
        $"{AbstractionsNamespace}.{LongRequestStreamHandlerAttributeName}";

    /// <summary>
    ///     Initializes the MediatorGenerator by registering post-initialization output with the provided
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

        var combinedProvider = allHandlerDetails.Combine(rootNamespaceProvider);

        context.RegisterSourceOutput(combinedProvider, static (ctx,
            tuple) =>
        {
            var (details, rootNamespace) = tuple;
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
                ctx.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "MDG002",
                        "No handler found",
                        "No handler found in this assembly. Use RequestHandlerAttribute or EventHandlerAttribute to mark a class as a handler.",
                        "Synapse.Generator",
                        DiagnosticSeverity.Info,
                        true),
                    Location.None));
            else
                foreach (var detail in details)
                    if (detail is null)
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "MDG004",
                                "Null handler found",
                                "Null handler found in this assembly. Use RequestHandlerAttribute or EventHandlerAttribute to mark a class as a handler.",
                                "Synapse.Generator",
                                DiagnosticSeverity.Warning,
                                true),
                            Location.None));
                    else
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "MDG003",
                                "Handler found",
                                $"Handler {detail.Value.ClassName}",
                                "Synapse.Generator",
                                DiagnosticSeverity.Info,
                                true),
                            detail.Value.Location ?? Location.None));

            ctx.AddSource("RegisterGroup.g.cs",
                RegisterGroupFactory.Create(rootNamespace, AbstractionsNamespace, details));
        });

        // Generate event dispatcher registrations for NativeAOT support
        var eventInfoProvider = compilationProvider
            .Select(static (compilation, _) => ExtractEventInfo(compilation));

        var eventDispatcherProvider = eventInfoProvider.Combine(rootNamespaceProvider);

        context.RegisterSourceOutput(eventDispatcherProvider, static (ctx, tuple) =>
        {
            var (eventInfo, rootNamespace) = tuple;

            if (string.IsNullOrEmpty(rootNamespace)) return;

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
                continue;

            if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
                // not a class
                continue;

            var className = classDeclaration.Identifier.ValueText;
            var @namespace = classDeclaration.GetNamespace();
            var (requestType, responseType) = GetRequestInfo(attribute);
            var location = classDeclaration.GetLocation();
            var handlerType = HandlerType.StreamRequestHandler;
            return new HandlerDetail(handlerType, className, @namespace, requestType, responseType, location);
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
                continue;

            if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
                // not a class
                continue;

            var className = classDeclaration.Identifier.ValueText;
            var @namespace = classDeclaration.GetNamespace();
            var (requestType, responseType) = GetRequestInfo(attribute);


            var location = classDeclaration.GetLocation();
            var handlerType = HandlerType.RequestHandler;
            return new HandlerDetail(handlerType, className, @namespace, requestType, responseType, location);
        }

        return null;
    }

    private static HandlerDetail? GetEventHandlerDetail(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attribute in ctx.Attributes)
        {
            if (!(attribute.AttributeClass?.Name is LongEventHandlerAttributeName or ShortEventHandlerAttributeName))
                // wrong attribute
                continue;

            if (ctx.TargetNode is not ClassDeclarationSyntax classDeclaration)
                // not a class
                continue;

            var className = classDeclaration.Identifier.ValueText;
            var @namespace = classDeclaration.GetNamespace();
            var (requestType, responseType) = GetRequestInfo(attribute);

            var location = classDeclaration.GetLocation();

            return new HandlerDetail(HandlerType.EventHandler, className, @namespace, requestType, responseType,
                location);
        }

        return null;
    }

    private static (string RequestType, string? ResponseType) GetRequestInfo(AttributeData attribute)
    {
        // Get the attribute constructor's type arguments
        var typeArgs = attribute.AttributeClass?.TypeArguments;
        if (typeArgs is null ||
            typeArgs.Value.Length == 0)
            return (string.Empty, null);


        // Get the fully qualified name of the request type
        var requestType = typeArgs.Value[0]
            .ToDisplayString();

        // Check if there's a response type (generic attribute with 2 type parameters)
        string? responseType = null;
        if (typeArgs.Value.Length > 1)
            responseType = typeArgs.Value[1]
                .ToDisplayString();

        return (requestType, responseType);
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

        if (iEventSymbol == null) return new EventInfo(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

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
            foreach (var member in symbol.GetMembers()) member.Accept(this);
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            // Check if this type implements IEvent
            if (ImplementsIEvent(symbol)) _eventTypes.Add(symbol.ToDisplayString());

            // Check if this type implements IEventHandler<T>
            if (_iEventHandlerSymbol != null && ImplementsIEventHandler(symbol))
                _handlerTypes.Add(symbol.ToDisplayString());

            // Visit nested types
            foreach (var nestedType in symbol.GetTypeMembers()) nestedType.Accept(this);
        }

        private bool ImplementsIEvent(INamedTypeSymbol typeSymbol)
        {
            // Skip abstract types and interfaces
            if (typeSymbol.IsAbstract || typeSymbol.TypeKind == TypeKind.Interface) return false;

            // Check all interfaces
            foreach (var @interface in typeSymbol.AllInterfaces)
                if (SymbolEqualityComparer.Default.Equals(@interface, _iEventSymbol))
                    return true;

            return false;
        }

        private bool ImplementsIEventHandler(INamedTypeSymbol typeSymbol)
        {
            // Skip abstract types and interfaces
            if (typeSymbol.IsAbstract || typeSymbol.TypeKind == TypeKind.Interface) return false;

            // Check all interfaces
            foreach (var @interface in typeSymbol.AllInterfaces)
                if (@interface.IsGenericType &&
                    SymbolEqualityComparer.Default.Equals(@interface.ConstructedFrom, _iEventHandlerSymbol))
                    return true;

            return false;
        }
    }
}