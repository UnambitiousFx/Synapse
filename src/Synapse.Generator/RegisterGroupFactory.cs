using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace UnambitiousFx.Synapse.Generator;

internal static class RegisterGroupFactory
{
    public static SourceText Create(string? rootNamespace,
        string abstractionsNamespace,
        ImmutableArray<HandlerDetail?> details,
        ImmutableArray<BehaviorDetail?> behaviors)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();

        sb.AppendLine($"public sealed class RegisterGroup : global::{abstractionsNamespace}.IRegisterGroup");
        sb.AppendLine("{");
        sb.AppendLine($"    public void Register(global::{abstractionsNamespace}.IDependencyInjectionBuilder builder)");
        sb.AppendLine("    {");

        // Emit handler registrations
        foreach (var detail in details)
        {
            if (detail is null)
            {
                continue;
            }

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

        // Emit behavior registrations (sorted by Order, then by original declaration order)
        var validBehaviors = behaviors
            .Where(b => b is not null)
            .Select(b => b!.Value)
            .OrderBy(b => b.Order)
            .ToArray();

        foreach (var behavior in validBehaviors)
        {
            if (behavior.IsOpenGeneric)
            {
                // Cross-product: emit one registration per matching handler
                EmitOpenGenericBehaviorRegistrations(sb, behavior, details);
            }
            else
            {
                // Closed behavior: emit a single explicit registration
                EmitClosedBehaviorRegistration(sb, behavior);
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return SourceText.From(sb.ToString(), Encoding.UTF8);
    }

    private static void EmitClosedBehaviorRegistration(StringBuilder sb, BehaviorDetail behavior)
    {
        var behaviorType = GlobalizeType(behavior.FullBehaviorTypeName);
        var requestType = GlobalizeType(behavior.FullRequestTypeName);

        switch (behavior.Kind)
        {
            case BehaviorKind.Request:
                sb.AppendLine(
                    $"        builder.RegisterRequestPipelineBehavior<{behaviorType}, {requestType}>();");
                break;
            case BehaviorKind.RequestWithResponse when behavior.FullResponseOrItemTypeName is { } responseType:
                sb.AppendLine(
                    $"        builder.RegisterRequestPipelineBehavior<{behaviorType}, {requestType}, {GlobalizeType(responseType)}>();");
                break;
            case BehaviorKind.Event:
                sb.AppendLine(
                    $"        builder.RegisterEventPipelineBehavior<{behaviorType}, {requestType}>();");
                break;
            case BehaviorKind.StreamRequest when behavior.FullResponseOrItemTypeName is { } itemType:
                sb.AppendLine(
                    $"        builder.RegisterStreamRequestPipelineBehavior<{behaviorType}, {requestType}, {GlobalizeType(itemType)}>();");
                break;
        }
    }

    private static void EmitOpenGenericBehaviorRegistrations(StringBuilder sb,
        BehaviorDetail behavior,
        ImmutableArray<HandlerDetail?> handlers)
    {
        var behaviorBaseName = GlobalizeType(behavior.FullBehaviorTypeName);

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            switch (behavior.Kind)
            {
                case BehaviorKind.Request when handler.Value.HandlerType == HandlerType.RequestHandler
                                               && handler.Value.FullResponseType is null:
                {
                    var req = GlobalizeType(handler.Value.FullTargetTypeName);
                    var closedBehavior = $"{behaviorBaseName}<{req}>";
                    sb.AppendLine($"        builder.RegisterRequestPipelineBehavior<{closedBehavior}, {req}>();");
                    break;
                }
                case BehaviorKind.RequestWithResponse when handler.Value.HandlerType == HandlerType.RequestHandler
                                                          && handler.Value.FullResponseType is { } resp:
                {
                    var req = GlobalizeType(handler.Value.FullTargetTypeName);
                    var respType = GlobalizeType(resp);
                    var closedBehavior = $"{behaviorBaseName}<{req}, {respType}>";
                    sb.AppendLine(
                        $"        builder.RegisterRequestPipelineBehavior<{closedBehavior}, {req}, {respType}>();");
                    break;
                }
                case BehaviorKind.Event when handler.Value.HandlerType == HandlerType.EventHandler:
                {
                    var evt = GlobalizeType(handler.Value.FullTargetTypeName);
                    var closedBehavior = $"{behaviorBaseName}<{evt}>";
                    sb.AppendLine($"        builder.RegisterEventPipelineBehavior<{closedBehavior}, {evt}>();");
                    break;
                }
                case BehaviorKind.StreamRequest when handler.Value.HandlerType == HandlerType.StreamRequestHandler
                                                    && handler.Value.FullResponseType is { } item:
                {
                    var req = GlobalizeType(handler.Value.FullTargetTypeName);
                    var itemType = GlobalizeType(item);
                    var closedBehavior = $"{behaviorBaseName}<{req}, {itemType}>";
                    sb.AppendLine(
                        $"        builder.RegisterStreamRequestPipelineBehavior<{closedBehavior}, {req}, {itemType}>();");
                    break;
                }
            }
        }
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
        {
            sb.AppendLine(
                $"        builder.RegisterRequestHandler<{GlobalizeType(detail.FullHandlerTypeName)}, {GlobalizeType(detail.FullTargetTypeName)}>();");
        }
        else
        {
            sb.AppendLine(
                $"        builder.RegisterRequestHandler<{GlobalizeType(detail.FullHandlerTypeName)}, {GlobalizeType(detail.FullTargetTypeName)}, {GlobalizeType(detail.FullResponseType)}>();");
        }
    }

    private static void RegisterStreamRequestHandler(StringBuilder sb,
        HandlerDetail detail)
    {
        // Streaming handlers always have a response type (the item type)
        if (detail.FullResponseType is not null)
        {
            sb.AppendLine(
                $"        builder.RegisterStreamRequestHandler<{GlobalizeType(detail.FullHandlerTypeName)}, {GlobalizeType(detail.FullTargetTypeName)}, {GlobalizeType(detail.FullResponseType)}>();");
        }
    }

    private static string GlobalizeType(string input)
    {
        if (input.Contains("<"))
        {
            // Already contains generic args — globalize the base and recurse on args
            var openIdx = input.IndexOf("<", StringComparison.Ordinal);
            var closeIdx = input.LastIndexOf(">", StringComparison.Ordinal);
            var baseType = input.Substring(0, openIdx);
            var innerArgs = input.Substring(openIdx + 1, closeIdx - openIdx - 1);
            return $"global::{baseType}<{innerArgs}>";
        }

        if (input.StartsWith("global::"))
        {
            return input;
        }

        return $"global::{input}";
    }
}
