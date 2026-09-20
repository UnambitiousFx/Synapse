using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN104: in an assembly where handlers are registered through the source generator's attributes, a handler class
///     without an attribute is left out of the generated registration. Manual registration cannot be seen, so this is a
///     heuristic; silence it for a hand-registered handler with <c>#pragma warning disable SYN104</c> or
///     <c>dotnet_diagnostic.SYN104.severity = none</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnattributedHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN104";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Handler is not registered by the source generator",
        "Handler '{0}' has no handler attribute, so the Synapse source generator does not register it. Add " +
        "[RequestHandler], [EventHandler] or [StreamRequestHandler], or register it by hand and silence this warning.",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "Other handlers in this assembly use the generator's attributes, so a handler without one is easy to forget.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!SynapseSymbols.ReferencesSynapse(start.Compilation))
            {
                return;
            }

            var unattributed = new ConcurrentBag<INamedTypeSymbol>();
            var anyAttributed = 0;

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind != TypeKind.Class)
                {
                    return;
                }

                if (SynapseSymbols.HasHandlerAttribute(type))
                {
                    Interlocked.Exchange(ref anyAttributed, 1);
                    return;
                }

                if (type.IsAbstract || type.IsGenericType)
                {
                    return;
                }

                if (type.AllInterfaces.Any(iface => SynapseSymbols.GetHandlerInterface(iface) is not null))
                {
                    unattributed.Add(type);
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(endContext =>
            {
                if (anyAttributed == 0)
                {
                    return;
                }

                foreach (var type in unattributed)
                {
                    var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                    if (location is not null)
                    {
                        endContext.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
                    }
                }
            });
        });
    }
}
