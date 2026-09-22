using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN105: <c>[assembly: SynapseGlobalBehavior]</c> is declared and this compilation calls
///     <c>AddSynapse(...)</c> somewhere (evidence it is a composition root), but no
///     <c>AddRegisterGroup(...)</c> call registers this compilation's own generated group — the default
///     <c>RegisterGroup</c> in the assembly's root namespace, or the class marked <c>[RegisterGroup]</c>.
///     The attribute is silently inert in that case: assembly attributes are not inherited across
///     references, so a downstream host cannot pick this up either. A compilation that declares the
///     attribute with no <c>AddSynapse(...)</c> call of its own (a shared library, not a host) is not
///     reported — a deliberate false negative, since the attribute is inert there regardless and this
///     analyzer only sees one compilation at a time.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GlobalBehaviorRegisterGroupAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN105";

    private const string DependencyInjectionExtensionsTypeName =
        "UnambitiousFx.Synapse.DependencyInjectionExtensions";

    private const string SynapseConfigInterfaceName = "UnambitiousFx.Synapse.ISynapseConfig";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Global behavior's generated group is never registered",
        "[assembly: SynapseGlobalBehavior] is declared but this assembly's generated RegisterGroup is never " +
        "passed to AddRegisterGroup, so the behavior never runs",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "AddSynapse(...) is called in this compilation, so it is a composition root, but no " +
        "AddRegisterGroup(...) call registers this assembly's own generated group. Call " +
        "cfg.AddRegisterGroup(new <ThisAssembly>.RegisterGroup()) alongside the other groups.",
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

            var hasAddSynapseCall = 0;
            var customTargets = new ConcurrentBag<(string Namespace, string ClassName)>();
            var registeredGroupTypes = new ConcurrentBag<ITypeSymbol>();

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.GetAttributes()
                    .Any(a => SynapseSymbols.IsAttribute(a.AttributeClass, "RegisterGroupAttribute")))
                {
                    customTargets.Add((type.ContainingNamespace.ToDisplayString(), type.Name));
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationAction(opContext =>
            {
                var invocation = (IInvocationOperation)opContext.Operation;
                var method = invocation.TargetMethod;

                if (IsMethod(method, DependencyInjectionExtensionsTypeName, "AddSynapse"))
                {
                    Interlocked.Exchange(ref hasAddSynapseCall, 1);
                    return;
                }

                if (IsMethod(method, SynapseConfigInterfaceName, "AddRegisterGroup"))
                {
                    var argumentType = invocation.Arguments.Length > 0
                        ? UnwrapConversions(invocation.Arguments[0].Value).Type
                        : null;
                    if (argumentType is not null)
                    {
                        registeredGroupTypes.Add(argumentType);
                    }
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(endContext =>
            {
                if (Volatile.Read(ref hasAddSynapseCall) == 0)
                {
                    return;
                }

                var globalEntryLocations = new List<Location>();
                foreach (var attribute in endContext.Compilation.Assembly.GetAttributes())
                {
                    if (!SynapseSymbols.IsAttribute(attribute.AttributeClass, "SynapseGlobalBehaviorAttribute"))
                    {
                        continue;
                    }

                    var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(endContext.CancellationToken);
                    if (syntax is null || SynapseSymbols.IsGenerated(syntax.SyntaxTree, endContext.CancellationToken))
                    {
                        continue;
                    }

                    globalEntryLocations.Add(syntax.GetLocation());
                }

                if (globalEntryLocations.Count == 0)
                {
                    return;
                }

                var rootNamespace = ResolveRootNamespace(start.Options, endContext.Compilation);
                var customTarget = customTargets.IsEmpty ? ((string, string)?)null : customTargets.First();
                var (expectedNamespace, expectedClassName) =
                    RegisterGroupNaming.Resolve(rootNamespace, customTarget);
                var expectedFullName = string.IsNullOrEmpty(expectedNamespace)
                    ? expectedClassName
                    : $"{expectedNamespace}.{expectedClassName}";

                var expectedType = endContext.Compilation.GetTypeByMetadataName(expectedFullName);
                if (expectedType is null)
                {
                    // Cannot resolve the expected type in this compilation view — prefer a false negative
                    // over guessing.
                    return;
                }

                var isRegistered = registeredGroupTypes.Any(t =>
                    SymbolEqualityComparer.Default.Equals(t, expectedType));
                if (isRegistered)
                {
                    return;
                }

                foreach (var location in globalEntryLocations)
                {
                    endContext.ReportDiagnostic(Diagnostic.Create(Rule, location));
                }
            });
        });
    }

    /// <summary>
    ///     Whether <paramref name="method" /> is the method named <paramref name="methodName" /> declared on
    ///     <paramref name="containingTypeName" />. Handles both an ordinary static call
    ///     (<c>DependencyInjectionExtensions.AddSynapse(services, cfg)</c>) and an extension-method call
    ///     (<c>services.AddSynapse(cfg)</c>) — for the latter, <see cref="IMethodSymbol.ContainingType" /> on
    ///     the reduced symbol already resolves to the declaring static class in Roslyn's API, but this checks
    ///     <see cref="IMethodSymbol.ReducedFrom" /> too so the match holds even if that changes.
    /// </summary>
    private static bool IsMethod(IMethodSymbol method, string containingTypeName, string methodName)
    {
        if (method.Name != methodName)
        {
            return false;
        }

        var original = method.ReducedFrom ?? method;
        return original.ContainingType.ToDisplayString() == containingTypeName;
    }

    /// <summary>
    ///     Unwraps the implicit reference conversion Roslyn's operation tree inserts around an argument whose
    ///     compile-time type differs from the parameter type — e.g. <c>AddRegisterGroup(new RegisterGroup())</c>
    ///     against a parameter of type <c>IRegisterGroup</c> is modelled as an <see cref="IConversionOperation" />
    ///     wrapping the <see cref="IObjectCreationOperation" />, whose own <see cref="IOperation.Type" /> is the
    ///     interface, not the concrete <c>RegisterGroup</c> class this rule needs to match against.
    /// </summary>
    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static string ResolveRootNamespace(AnalyzerOptions options, Compilation compilation)
    {
        if (options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace",
                out var rootNamespace) && !string.IsNullOrWhiteSpace(rootNamespace))
        {
            return rootNamespace;
        }

        return compilation.GetRootNamespaceFromAssemblyAttributes();
    }
}
