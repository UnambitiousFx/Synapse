using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace UnambitiousFx.Synapse.Generator;

internal static class RegisterGroupFactory
{
    public static SourceText Create(string? rootNamespace,
        string abstractionsNamespace,
        ImmutableArray<HandlerDetail?> details,
        ImmutableArray<BehaviorDetail> behaviors)
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

        // Emit behavior registrations. Sort by Order, then by a deterministic secondary key
        // (namespace + class name) so behaviors that share an Order have a stable, reproducible
        // pipeline position independent of discovery order across (incremental) compilations.
        var validBehaviors = behaviors
            .OrderBy(b => b.Order)
            .ThenBy(b => b.Namespace, StringComparer.Ordinal)
            .ThenBy(b => b.ClassName, StringComparer.Ordinal)
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
                                               && handler.Value.FullResponseType is null
                                               && Satisfies(behavior.RequestConstraints,
                                                   handler.Value.TargetSatisfyingTypes):
                {
                    var req = GlobalizeType(handler.Value.FullTargetTypeName);
                    var closedBehavior = $"{behaviorBaseName}<{req}>";
                    sb.AppendLine($"        builder.RegisterRequestPipelineBehavior<{closedBehavior}, {req}>();");
                    break;
                }
                case BehaviorKind.RequestWithResponse when handler.Value.HandlerType == HandlerType.RequestHandler
                                                          && handler.Value.FullResponseType is { } resp
                                                          && Satisfies(behavior.RequestConstraints,
                                                              handler.Value.TargetSatisfyingTypes)
                                                          && Satisfies(behavior.ResponseConstraints,
                                                              handler.Value.ResponseSatisfyingTypes):
                {
                    var req = GlobalizeType(handler.Value.FullTargetTypeName);
                    var respType = GlobalizeType(resp);
                    var closedBehavior = $"{behaviorBaseName}<{req}, {respType}>";
                    sb.AppendLine(
                        $"        builder.RegisterRequestPipelineBehavior<{closedBehavior}, {req}, {respType}>();");
                    break;
                }
                case BehaviorKind.Event when handler.Value.HandlerType == HandlerType.EventHandler
                                             && Satisfies(behavior.RequestConstraints,
                                                 handler.Value.TargetSatisfyingTypes):
                {
                    var evt = GlobalizeType(handler.Value.FullTargetTypeName);
                    var closedBehavior = $"{behaviorBaseName}<{evt}>";
                    sb.AppendLine($"        builder.RegisterEventPipelineBehavior<{closedBehavior}, {evt}>();");
                    break;
                }
                case BehaviorKind.StreamRequest when handler.Value.HandlerType == HandlerType.StreamRequestHandler
                                                    && handler.Value.FullResponseType is { } item
                                                    && Satisfies(behavior.RequestConstraints,
                                                        handler.Value.TargetSatisfyingTypes)
                                                    && Satisfies(behavior.ResponseConstraints,
                                                        handler.Value.ResponseSatisfyingTypes):
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

    /// <summary>
    ///     Returns true when every named-type constraint is present in the candidate type's set of
    ///     satisfying types (itself + base types + interfaces). No constraints means every type qualifies.
    /// </summary>
    private static bool Satisfies(EquatableArray<string> constraints, EquatableArray<string> satisfyingTypes)
    {
        if (constraints.Count == 0)
        {
            return true;
        }

        foreach (var constraint in constraints)
        {
            var matched = false;
            foreach (var candidate in satisfyingTypes)
            {
                if (string.Equals(candidate, constraint, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    // C# predefined type keywords — global:: prefix is not valid for these.
    private static readonly HashSet<string> CSharpKeywordTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "bool", "byte", "char", "decimal", "double", "float", "int", "long",
        "nint", "nuint", "object", "sbyte", "short", "string", "uint", "ulong", "ushort", "void"
    };

    private static string GlobalizeType(string input)
    {
        input = input.Trim();

        var openIdx = input.IndexOf('<');
        if (openIdx >= 0)
        {
            // Generic type: globalize the base, recurse into each top-level type argument, and
            // preserve any trailing decorators (e.g. nullable '?' or array '[]') after the '>'.
            var closeIdx = input.LastIndexOf('>');
            var baseType = input.Substring(0, openIdx);
            var argsText = input.Substring(openIdx + 1, closeIdx - openIdx - 1);
            var suffix = input.Substring(closeIdx + 1);
            var args = string.Join(", ", SplitTopLevelArgs(argsText).Select(GlobalizeType));
            return $"{GlobalizeSimpleType(baseType)}<{args}>{suffix}";
        }

        return GlobalizeSimpleType(input);
    }

    private static string GlobalizeSimpleType(string input)
    {
        // Split trailing decorators ('?', '[]') from the core type name so keyword detection works.
        var coreLength = input.Length;
        while (coreLength > 0 && (input[coreLength - 1] == '?' || input[coreLength - 1] == '[' ||
                                  input[coreLength - 1] == ']'))
        {
            coreLength--;
        }

        var core = input.Substring(0, coreLength);
        var suffix = input.Substring(coreLength);

        if (core.StartsWith("global::", StringComparison.Ordinal) || CSharpKeywordTypes.Contains(core))
        {
            return core + suffix;
        }

        return $"global::{core}{suffix}";
    }

    private static IEnumerable<string> SplitTopLevelArgs(string args)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(args.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }

        result.Add(args.Substring(start));
        return result;
    }
}
