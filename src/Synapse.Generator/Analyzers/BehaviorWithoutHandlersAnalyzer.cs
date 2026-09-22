using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN102: a <c>[PipelineBehavior]</c> class or an <c>[assembly: SynapseGlobalBehavior]</c> entry that the generator
///     would emit for no handler, so it silently never runs. Handlers visible to a behavior are those of this
///     compilation plus those of referenced assemblies that themselves reference Synapse.Abstractions. When a
///     behavior cannot be evaluated (special constraints, constraints over other type parameters) it is not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BehaviorWithoutHandlersAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN102";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Pipeline behavior applies to no handler",
        "Pipeline behavior '{0}' matches no handler visible from this assembly, so it never runs",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "Behaviors are only registered for handlers found in this assembly and the assemblies it references. Check " +
        "the request or event type the behavior targets and the handlers this assembly can see.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!SynapseSymbols.ReferencesSynapse(start.Compilation))
            {
                return;
            }

            var behaviors = new ConcurrentBag<INamedTypeSymbol>();
            var sourceHandlers = new ConcurrentBag<MessageInterface>();

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                {
                    return;
                }

                if (type.GetAttributes().Any(a => SynapseSymbols.IsAttribute(a.AttributeClass, "PipelineBehaviorAttribute")))
                {
                    behaviors.Add(type);
                }

                if (type.IsAbstract)
                {
                    return;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (SynapseSymbols.GetHandlerInterface(iface) is { } handler)
                    {
                        sourceHandlers.Add(handler);
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(endContext =>
            {
                var globalEntries = new List<(INamedTypeSymbol Type, Location Location)>();
                foreach (var attribute in endContext.Compilation.Assembly.GetAttributes())
                {
                    if (!SynapseSymbols.IsAttribute(attribute.AttributeClass, "SynapseGlobalBehaviorAttribute")
                        || attribute.ConstructorArguments.Length != 1
                        || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol behaviorType)
                    {
                        continue;
                    }

                    // A behavior declared in a referenced assembly is usually paired with a sibling of another arity
                    // (open-generic pair) whose handlers this rule cannot attribute, so only local behaviors are checked.
                    if (!SymbolEqualityComparer.Default.Equals(behaviorType.ContainingAssembly,
                            endContext.Compilation.Assembly))
                    {
                        continue;
                    }

                    var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(endContext.CancellationToken);
                    if (syntax is null || SynapseSymbols.IsGenerated(syntax.SyntaxTree, endContext.CancellationToken))
                    {
                        continue;
                    }

                    globalEntries.Add((behaviorType.OriginalDefinition, syntax.GetLocation()));
                }

                if (behaviors.IsEmpty && globalEntries.Count == 0)
                {
                    return;
                }

                var handlers = sourceHandlers.ToList();
                handlers.AddRange(ReferencedHandlers(endContext.Compilation, endContext.CancellationToken));

                foreach (var behavior in behaviors)
                {
                    var location = SynapseSymbols.GetReportableLocation(behavior, endContext.CancellationToken);
                    if (location is not null)
                    {
                        Check(endContext, behavior, location, handlers);
                    }
                }

                foreach (var (type, location) in globalEntries)
                {
                    Check(endContext, type, location, handlers);
                }
            });
        });
    }

    private static void Check(CompilationAnalysisContext context, INamedTypeSymbol behavior, Location location,
        List<MessageInterface> handlers)
    {
        var recognized = false;
        foreach (var iface in behavior.AllInterfaces)
        {
            if (SynapseSymbols.GetBehaviorInterface(iface) is not { } pipeline)
            {
                continue;
            }

            recognized = true;
            if (MayApply(context.Compilation, pipeline, handlers))
            {
                return;
            }
        }

        if (recognized)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, behavior.Name));
        }
    }

    private static bool MayApply(Compilation compilation, MessageInterface pipeline, List<MessageInterface> handlers)
    {
        var candidates = handlers.Where(handler => handler.Kind == pipeline.Kind).ToList();
        var target = pipeline.MessageType;

        if (target is ITypeParameterSymbol parameter)
        {
            if (parameter.HasReferenceTypeConstraint || parameter.HasValueTypeConstraint
                                                     || parameter.HasConstructorConstraint
                                                     || parameter.HasNotNullConstraint
                                                     || parameter.HasUnmanagedTypeConstraint)
            {
                return true;
            }

            var constraints = parameter.ConstraintTypes.Where(constraint => !SynapseSymbols.ContainsTypeParameter(constraint)).ToList();
            return candidates.Any(handler => constraints.All(constraint =>
                compilation.ClassifyCommonConversion(handler.MessageType, constraint).IsImplicit));
        }

        if (SynapseSymbols.ContainsTypeParameter(target))
        {
            return true;
        }

        return candidates.Any(handler => SymbolEqualityComparer.Default.Equals(handler.MessageType, target));
    }

    private static IEnumerable<MessageInterface> ReferencedHandlers(Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly.Name == SynapseSymbols.AbstractionsAssemblyName
                || !assembly.Modules.Any(module => module.ReferencedAssemblies.Any(reference =>
                    reference.Name == SynapseSymbols.AbstractionsAssemblyName)))
            {
                continue;
            }

            foreach (var type in SynapseSymbols.GetTypes(assembly.GlobalNamespace, cancellationToken))
            {
                // Only public types are handlers a consumer could plausibly have declared: an internal type in a
                // referenced assembly (e.g. Synapse's own ProxyRequestHandler<,> plumbing) is implementation detail,
                // never something this analyzer should count as "a real handler this behavior applies to".
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract
                    || type.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (SynapseSymbols.GetHandlerInterface(iface) is { } handler)
                    {
                        yield return handler;
                    }
                }
            }
        }
    }
}
