using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN103: a handler attribute on a <c>record</c>. The source generator only accepts class declarations, so the
///     attribute is ignored without any error and the handler is never registered.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerAttributeOnRecordAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN103";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Handler attribute on a record is ignored",
        "[{0}] on record '{1}' is ignored by the Synapse source generator; declare the handler as a class",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "The Synapse source generator only registers handlers declared as classes. Declaring the handler as a record " +
        "leaves it unregistered without any other diagnostic.");

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

            start.RegisterSyntaxNodeAction(AnalyzeRecord, SyntaxKind.RecordDeclaration);
        });
    }

    private static void AnalyzeRecord(SyntaxNodeAnalysisContext context)
    {
        var record = (RecordDeclarationSyntax)context.Node;
        foreach (var attributeList in record.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var constructor = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
                if (constructor?.ContainingType is not { } attributeClass
                    || !SynapseSymbols.IsHandlerAttribute(attributeClass))
                {
                    continue;
                }

                var name = attributeClass.Name.EndsWith("Attribute", StringComparison.Ordinal)
                    ? attributeClass.Name.Substring(0, attributeClass.Name.Length - "Attribute".Length)
                    : attributeClass.Name;
                context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.GetLocation(), name,
                    record.Identifier.ValueText));
            }
        }
    }
}
