using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Factory for generating event dispatcher registrations for NativeAOT compatibility.
/// </summary>
internal static class EventDispatcherRegistrationFactory
{
    public static SourceText Create(
        string? rootNamespace,
        string abstractionsNamespace,
        EventInfo eventInfo)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Diagnostics.CodeAnalysis;");
        sb.AppendLine($"using global::{abstractionsNamespace};");
        sb.AppendLine("using UnambitiousFx.Functional.Results;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     Generated event dispatcher registrations for NativeAOT compatibility.");
        sb.AppendLine(
            "///     This class provides typed delegates for each event type to avoid reflection during outbox replay.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine(
            $"public sealed class EventDispatcherRegistration : global::{abstractionsNamespace}.IEventDispatcherRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     Registers dispatcher delegates for all event types in the assembly.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine(
            "    /// <param name=\"register\">A callback to register each event type with its dispatcher delegate.</param>");
        sb.AppendLine("    public void RegisterDispatchers(Action<Type, Delegate> register)");
        sb.AppendLine("    {");

        foreach (var eventType in eventInfo.EventTypes)
        {
            if (string.IsNullOrWhiteSpace(eventType)) continue;

            var globalizedType = GlobalizeType(eventType);

            sb.AppendLine(
                $"        register(typeof({globalizedType}), new Func<global::{abstractionsNamespace}.IEvent, global::{abstractionsNamespace}.IEventDispatcher, DistributionMode, CancellationToken, ValueTask<Result>>(");
            sb.AppendLine("            async (@event, dispatcher, distributionMode, ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine($"                var typedEvent = ({globalizedType})@event;");
            sb.AppendLine(
                "                return await dispatcher.DispatchAsync(typedEvent, global::UnambitiousFx.Synapse.Abstractions.DistributionMode.Undefined, ct);");
            sb.AppendLine("            }));");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // Generate DynamicDependency attributes for trimming support
        GenerateDynamicDependencyAttributes(sb, abstractionsNamespace, eventInfo);

        sb.AppendLine("}");

        return SourceText.From(sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateDynamicDependencyAttributes(
        StringBuilder sb,
        string abstractionsNamespace,
        EventInfo eventInfo)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     Preserves event types and their handlers for trimming.");
        sb.AppendLine("    ///     This method is never called but ensures the trimmer preserves necessary types.");
        sb.AppendLine("    /// </summary>");

        // Preserve event types
        foreach (var eventType in eventInfo.EventTypes)
        {
            if (string.IsNullOrWhiteSpace(eventType)) continue;

            var globalizedType = GlobalizeType(eventType);
            sb.AppendLine($"    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof({globalizedType}))]");
        }

        // Preserve handler types
        foreach (var handlerType in eventInfo.HandlerTypes)
        {
            if (string.IsNullOrWhiteSpace(handlerType)) continue;

            var globalizedType = GlobalizeType(handlerType);
            sb.AppendLine($"    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof({globalizedType}))]");
        }

        // Preserve core mediator types
        sb.AppendLine(
            $"    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(global::{abstractionsNamespace}.IEventDispatcher))]");
        sb.AppendLine(
            $"    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(global::{abstractionsNamespace}.IEventHandler<>))]");
        sb.AppendLine("    private static void PreserveTypesForTrimming()");
        sb.AppendLine("    {");
        sb.AppendLine("        // This method is never called but ensures the trimmer preserves necessary types");
        sb.AppendLine("    }");
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