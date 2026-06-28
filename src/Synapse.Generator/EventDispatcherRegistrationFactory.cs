using System.Text;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Helper that emits the <c>RegisterDispatchers</c> method and NativeAOT <c>[DynamicDependency]</c>
///     attributes into a <see cref="StringBuilder" /> owned by <see cref="RegisterGroupFactory" />.
/// </summary>
internal static class EventDispatcherRegistrationFactory
{
    /// <summary>
    ///     Emits the <c>RegisterDispatchers</c> method implementation into <paramref name="sb" />.
    ///     The caller is responsible for the surrounding class context.
    /// </summary>
    public static void EmitRegisterDispatchers(
        StringBuilder sb,
        string abstractionsNamespace,
        EventInfo eventInfo)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     Registers dispatcher delegates for all event types in the assembly.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine(
            "    /// <param name=\"register\">A callback to register each event type with its dispatcher delegate.</param>");
        sb.AppendLine(
            $"    public void RegisterDispatchers(global::System.Action<global::System.Type, global::{abstractionsNamespace}.DispatchEventDelegate> register)");
        sb.AppendLine("    {");

        foreach (var eventType in eventInfo.EventTypes)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                continue;
            }

            var globalizedType = GlobalizeType(eventType);

            sb.AppendLine(
                $"        register(typeof({globalizedType}), new global::{abstractionsNamespace}.DispatchEventDelegate(");
            sb.AppendLine("            (@event, dispatcher, ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine($"                var typedEvent = ({globalizedType})@event;");
            sb.AppendLine(
                "                return dispatcher.DispatchAsync(typedEvent, ct);");
            sb.AppendLine("            }));");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        EmitDynamicDependencyAttributes(sb, abstractionsNamespace, eventInfo);
    }

    private static void EmitDynamicDependencyAttributes(
        StringBuilder sb,
        string abstractionsNamespace,
        EventInfo eventInfo)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     Preserves event types and their handlers for trimming.");
        sb.AppendLine("    ///     This method is never called but ensures the trimmer preserves necessary types.");
        sb.AppendLine("    /// </summary>");

        foreach (var eventType in eventInfo.EventTypes)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                continue;
            }

            var globalizedType = GlobalizeType(eventType);
            sb.AppendLine(
                $"    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof({globalizedType}))]");
        }

        foreach (var handlerType in eventInfo.HandlerTypes)
        {
            if (string.IsNullOrWhiteSpace(handlerType))
            {
                continue;
            }

            var globalizedType = GlobalizeType(handlerType);
            sb.AppendLine(
                $"    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof({globalizedType}))]");
        }

        sb.AppendLine(
            $"    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(global::{abstractionsNamespace}.IEventDispatcher))]");
        sb.AppendLine(
            $"    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(global::{abstractionsNamespace}.IEventHandler<>))]");
        sb.AppendLine("    private static void PreserveTypesForTrimming()");
        sb.AppendLine("    {");
        sb.AppendLine("        // This method is never called but ensures the trimmer preserves necessary types");
        sb.AppendLine("    }");
    }

    internal static string GlobalizeType(string input)
    {
        if (input.Contains("<"))
        {
            var genericType = input.Substring(0, input.IndexOf("<", StringComparison.Ordinal));
            var underlyingType = input.Substring(input.IndexOf("<", StringComparison.Ordinal) + 1,
                input.IndexOf(">", StringComparison.Ordinal) - input.IndexOf("<", StringComparison.Ordinal) - 1);
            return $"global::{genericType}<global::{underlyingType}>";
        }

        if (input.StartsWith("global::"))
        {
            return input;
        }

        return $"global::{input}";
    }
}
