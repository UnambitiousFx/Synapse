using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace UnambitiousFx.Synapse.Generator;

internal static class RegisterGroupFactory
{
    public static SourceText Create(string? rootNamespace,
        string abstractionsNamespace,
        ImmutableArray<HandlerDetail?> details)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();

        sb.AppendLine($"public sealed class RegisterGroup : global::{abstractionsNamespace}.IRegisterGroup");
        sb.AppendLine("{");
        sb.AppendLine($"    public void Register(global::{abstractionsNamespace}.IDependencyInjectionBuilder builder)");
        sb.AppendLine("    {");
        foreach (var detail in details)
        {
            if (detail is null) continue;

            switch (detail.Value.HandlerType)
            {
                case HandlerType.RequestHandler:
                    RegisterRequestHandler(sb, detail.Value);
                    break;
                case HandlerType.EventHandler:
                    RegisterEventHandler(sb, detail.Value);
                    break;
                case HandlerType.StreamRequestHandler:
                    RegisterStreamRequestHandler(sb, detail.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return SourceText.From(sb.ToString(), Encoding.UTF8);
    }

    private static void RegisterEventHandler(StringBuilder sb,
        HandlerDetail detail)
    {
        sb.AppendLine(
            $"        builder.RegisterEventHandler<{GlobalizeType(detail.FullHandlerTypeName)}, {GlobalizeType(detail.FullTargetTypeName)}>();");
    }

    private static void RegisterRequestHandler(StringBuilder sb,
        HandlerDetail detail)
    {
        if (detail.FullResponseType is null)
            sb.AppendLine(
                $"        builder.RegisterRequestHandler<{GlobalizeType(detail.FullHandlerTypeName)}, {GlobalizeType(detail.FullTargetTypeName)}>();");
        else
            sb.AppendLine(
                $"        builder.RegisterRequestHandler<{GlobalizeType(detail.FullHandlerTypeName)}, {GlobalizeType(detail.FullTargetTypeName)}, {GlobalizeType(detail.FullResponseType)}>();");
    }

    private static void RegisterStreamRequestHandler(StringBuilder sb,
        HandlerDetail detail)
    {
        // Streaming handlers always have a response type (the item type)
        if (detail.FullResponseType is not null)
            sb.AppendLine(
                $"        builder.RegisterStreamRequestHandler<{GlobalizeType(detail.FullHandlerTypeName)}, {GlobalizeType(detail.FullTargetTypeName)}, {GlobalizeType(detail.FullResponseType)}>();");
    }

    private static string GlobalizeType(string input)
    {
        if (input.Contains("<"))
        {
            var genericType = input.Substring(0, input.IndexOf("<", StringComparison.Ordinal));
            var underlyingType = input.Substring(input.IndexOf("<", StringComparison.Ordinal) + 1,
                input.IndexOf(">", StringComparison.Ordinal) - input.IndexOf("<", StringComparison.Ordinal) - 1);
            return $"global::{genericType}<global::{underlyingType}>";
        }

        if (input.StartsWith("global::")) return input;

        return $"global::{input}";
    }
}