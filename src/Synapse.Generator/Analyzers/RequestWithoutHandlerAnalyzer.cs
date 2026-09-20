using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN101: a request type with no handler in this compilation. Only the current assembly is scanned, so a handler
///     that lives in another project is a false positive; disable the rule there with
///     <c>dotnet_diagnostic.SYN101.severity = none</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequestWithoutHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN101";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Request has no handler in this assembly",
        "Request '{0}' has no handler in this assembly",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "No type in this assembly implements IRequestHandler for this request, so invoking it fails at runtime. If " +
        "the handler lives in another assembly, disable this rule with dotnet_diagnostic.SYN101.severity = none.",
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

            var requests = new ConcurrentBag<INamedTypeSymbol>();
            var handled = new ConcurrentDictionary<ISymbol, byte>(SymbolEqualityComparer.Default);
            var hasGenericHandler = 0;

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                {
                    return;
                }

                if (!type.IsAbstract && !type.IsGenericType && type.AllInterfaces.Any(SynapseSymbols.IsRequestInterface))
                {
                    requests.Add(type);
                }

                foreach (var iface in type.AllInterfaces)
                {
                    var handler = SynapseSymbols.GetHandlerInterface(iface);
                    if (handler is not { Kind: HandlerKind.Request or HandlerKind.RequestWithResponse } request)
                    {
                        continue;
                    }

                    if (request.MessageType is ITypeParameterSymbol || request.MessageType is INamedTypeSymbol { IsGenericType: true })
                    {
                        Interlocked.Exchange(ref hasGenericHandler, 1);
                        continue;
                    }

                    handled.TryAdd(request.MessageType, 0);
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(endContext =>
            {
                if (hasGenericHandler == 1)
                {
                    return;
                }

                foreach (var request in requests)
                {
                    if (handled.ContainsKey(request))
                    {
                        continue;
                    }

                    var location = request.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                    if (location is null)
                    {
                        continue;
                    }

                    endContext.ReportDiagnostic(Diagnostic.Create(Rule, location, request.Name));
                }
            });
        });
    }
}
