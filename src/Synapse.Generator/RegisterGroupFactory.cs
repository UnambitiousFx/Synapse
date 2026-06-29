using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace UnambitiousFx.Synapse.Generator;

internal static class RegisterGroupFactory
{
    public static SourceText Create(string? rootNamespace,
        string abstractionsNamespace,
        ImmutableArray<HandlerDetail?> details,
        ImmutableArray<BehaviorDetail> behaviors,
        ImmutableArray<HandlerDetail> behaviorTargets,
        bool crossAssemblyBehaviorsDisabled,
        ImmutableArray<ValidatorDetail> validators,
        EventInfo eventInfo)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();

        sb.AppendLine(
            $"public sealed class RegisterGroup : global::{abstractionsNamespace}.IRegisterGroup, global::{abstractionsNamespace}.IEventDispatcherRegistration");
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

        // Behaviors (including the built-in CQRS enforcement behavior, now a global behavior) are applied to
        // every handler target visible in the compilation (own assembly + referenced assemblies), so
        // registering them in the composition root covers the whole reference graph. The opt-out attribute
        // restricts this to same-assembly handlers. Handler registration always stays scoped to `details`
        // (this assembly) so handlers aren't double-registered; behavior registration is deduplicated at the
        // service-collection level, so emitting it for a referenced request that its own assembly also covers
        // is harmless.
        var openGenericTargets = crossAssemblyBehaviorsDisabled
            ? details.Where(d => d is not null).Select(d => d!.Value).ToArray()
            : behaviorTargets.ToArray();

        // Emit behavior registrations in a deterministic order (namespace + class name) so the generated
        // source is reproducible across (incremental) compilations. Runtime pipeline position is decided
        // by IOrderedPipelineBehavior, not emit order — this only stabilizes the registration sequence,
        // which the runtime stable sort falls back to for behaviors that share an Order.
        var validBehaviors = behaviors
            .OrderBy(b => b.Namespace, StringComparer.Ordinal)
            .ThenBy(b => b.ClassName, StringComparer.Ordinal)
            .ToArray();

        foreach (var behavior in validBehaviors)
        {
            if (behavior.IsOpenGeneric)
            {
                // Cross-product: emit one registration per matching handler
                EmitOpenGenericBehaviorRegistrations(sb, behavior, openGenericTargets);
            }
            else
            {
                // Closed behavior: emit a single explicit registration
                EmitClosedBehaviorRegistration(sb, behavior);
            }
        }

        // Emit validator registrations last so the validation behavior lands innermost (closest to the
        // handler). Each call registers the validator and the closed RequestValidationBehavior that runs it;
        // the behavior registration is deduplicated, so multiple validators for one request wire it once.
        EmitValidatorRegistrations(sb, validators);

        sb.AppendLine("    }");
        sb.AppendLine();

        EventDispatcherRegistrationFactory.EmitRegisterDispatchers(sb, abstractionsNamespace, eventInfo);

        sb.AppendLine("}");
        return SourceText.From(sb.ToString(), Encoding.UTF8);
    }

    private static void EmitValidatorRegistrations(StringBuilder sb, ImmutableArray<ValidatorDetail> validators)
    {
        foreach (var validator in validators)
        {
            var validatorType = validator.FullValidatorTypeName;
            var requestType = validator.FullRequestTypeName;

            if (validator.FullResponseType is { } responseType)
            {
                sb.AppendLine(
                    $"        builder.RegisterValidator<{validatorType}, {requestType}, {responseType}>();");
            }
            else
            {
                sb.AppendLine(
                    $"        builder.RegisterValidator<{validatorType}, {requestType}>();");
            }
        }
    }

    private static void EmitClosedBehaviorRegistration(StringBuilder sb, BehaviorDetail behavior)
    {
        var behaviorType = behavior.FullBehaviorTypeName;
        var requestType = behavior.FullRequestTypeName;

        switch (behavior.Kind)
        {
            case BehaviorKind.Request:
                sb.AppendLine(
                    $"        builder.RegisterRequestPipelineBehavior<{behaviorType}, {requestType}>();");
                break;
            case BehaviorKind.RequestWithResponse when behavior.FullResponseOrItemTypeName is { } responseType:
                sb.AppendLine(
                    $"        builder.RegisterRequestPipelineBehavior<{behaviorType}, {requestType}, {responseType}>();");
                break;
            case BehaviorKind.Event:
                sb.AppendLine(
                    $"        builder.RegisterEventPipelineBehavior<{behaviorType}, {requestType}>();");
                break;
            case BehaviorKind.StreamRequest when behavior.FullResponseOrItemTypeName is { } itemType:
                sb.AppendLine(
                    $"        builder.RegisterStreamRequestPipelineBehavior<{behaviorType}, {requestType}, {itemType}>();");
                break;
        }
    }

    private static void EmitOpenGenericBehaviorRegistrations(StringBuilder sb,
        BehaviorDetail behavior,
        IReadOnlyList<HandlerDetail> handlers)
    {
        var behaviorBaseName = behavior.FullBehaviorTypeName;

        foreach (var handler in handlers)
        {
            switch (behavior.Kind)
            {
                case BehaviorKind.Request when handler.HandlerType == HandlerType.RequestHandler
                                               && handler.FullResponseType is null
                                               && Satisfies(behavior.RequestConstraints,
                                                   handler.TargetSatisfyingTypes)
                                               && SatisfiesSpecial(behavior.RequestSpecialConstraints,
                                                   handler.RequestShape):
                {
                    var req = handler.FullTargetTypeName;
                    var closedBehavior = CloseBehavior(behavior, behaviorBaseName, req, null);
                    sb.AppendLine($"        builder.RegisterRequestPipelineBehavior<{closedBehavior}, {req}>();");
                    break;
                }
                case BehaviorKind.RequestWithResponse when handler.HandlerType == HandlerType.RequestHandler
                                                          && handler.FullResponseType is { } resp
                                                          && Satisfies(behavior.RequestConstraints,
                                                              handler.TargetSatisfyingTypes)
                                                          && Satisfies(behavior.ResponseConstraints,
                                                              handler.ResponseSatisfyingTypes)
                                                          && SatisfiesSpecial(behavior.RequestSpecialConstraints,
                                                              handler.RequestShape)
                                                          && SatisfiesSpecial(behavior.ResponseSpecialConstraints,
                                                              handler.ResponseShape):
                {
                    var req = handler.FullTargetTypeName;
                    var respType = resp;
                    var closedBehavior = CloseBehavior(behavior, behaviorBaseName, req, respType);
                    sb.AppendLine(
                        $"        builder.RegisterRequestPipelineBehavior<{closedBehavior}, {req}, {respType}>();");
                    break;
                }
                case BehaviorKind.Event when handler.HandlerType == HandlerType.EventHandler
                                             && Satisfies(behavior.RequestConstraints,
                                                 handler.TargetSatisfyingTypes)
                                             && SatisfiesSpecial(behavior.RequestSpecialConstraints,
                                                 handler.RequestShape):
                {
                    var evt = handler.FullTargetTypeName;
                    var closedBehavior = CloseBehavior(behavior, behaviorBaseName, evt, null);
                    sb.AppendLine($"        builder.RegisterEventPipelineBehavior<{closedBehavior}, {evt}>();");
                    break;
                }
                case BehaviorKind.StreamRequest when handler.HandlerType == HandlerType.StreamRequestHandler
                                                    && handler.FullResponseType is { } item
                                                    && Satisfies(behavior.RequestConstraints,
                                                        handler.TargetSatisfyingTypes)
                                                    && Satisfies(behavior.ResponseConstraints,
                                                        handler.ResponseSatisfyingTypes)
                                                    && SatisfiesSpecial(behavior.RequestSpecialConstraints,
                                                        handler.RequestShape)
                                                    && SatisfiesSpecial(behavior.ResponseSpecialConstraints,
                                                        handler.ResponseShape):
                {
                    var req = handler.FullTargetTypeName;
                    var itemType = item;
                    var closedBehavior = CloseBehavior(behavior, behaviorBaseName, req, itemType);
                    sb.AppendLine(
                        $"        builder.RegisterStreamRequestPipelineBehavior<{closedBehavior}, {req}, {itemType}>();");
                    break;
                }
            }
        }
    }

    /// <summary>
    ///     Builds the closed behavior type name by substituting each of the class's own type parameters with
    ///     the concrete request/response type, in class-declaration order. The order and arity come from
    ///     <see cref="BehaviorDetail.ClosingTypeArgumentMap" /> (index <c>0</c> → request/event, <c>1</c> →
    ///     response/item) so a class whose generic arity or parameter order differs from its interface closes
    ///     correctly. Falls back to the interface arity when no map is recorded (legacy / safety).
    /// </summary>
    private static string CloseBehavior(BehaviorDetail behavior, string behaviorBaseName, string request,
        string? response)
    {
        if (behavior.ClosingTypeArgumentMap.Count == 0)
        {
            return response is null
                ? $"{behaviorBaseName}<{request}>"
                : $"{behaviorBaseName}<{request}, {response}>";
        }

        var args = behavior.ClosingTypeArgumentMap.Select(i => i == 1 ? response! : request);
        return $"{behaviorBaseName}<{string.Join(", ", args)}>";
    }

    private static void RegisterEventHandler(StringBuilder sb,
        HandlerDetail detail)
    {
        sb.AppendLine(
            $"        builder.RegisterEventHandler<{detail.FullHandlerTypeName}, {detail.FullTargetTypeName}>();");
    }

    private static void RegisterRequestHandler(StringBuilder sb,
        HandlerDetail detail)
    {
        if (detail.FullResponseType is null)
        {
            sb.AppendLine(
                $"        builder.RegisterRequestHandler<{detail.FullHandlerTypeName}, {detail.FullTargetTypeName}>();");
        }
        else
        {
            sb.AppendLine(
                $"        builder.RegisterRequestHandler<{detail.FullHandlerTypeName}, {detail.FullTargetTypeName}, {detail.FullResponseType}>();");
        }
    }

    private static void RegisterStreamRequestHandler(StringBuilder sb,
        HandlerDetail detail)
    {
        // Streaming handlers always have a response type (the item type)
        if (detail.FullResponseType is not null)
        {
            sb.AppendLine(
                $"        builder.RegisterStreamRequestHandler<{detail.FullHandlerTypeName}, {detail.FullTargetTypeName}, {detail.FullResponseType}>();");
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

    /// <summary>
    ///     Returns true when the candidate type's shape satisfies every special (non-type) constraint the
    ///     behavior places on the corresponding type parameter — <c>class</c>/<c>struct</c>/<c>unmanaged</c>/
    ///     <c>notnull</c>/<c>new()</c>. Without this, closing an open-generic behavior over a non-conforming
    ///     handler emits code that fails to compile (e.g. CS0453 for a <c>struct</c>-constrained parameter
    ///     bound to a reference type). No special constraints means every type qualifies.
    /// </summary>
    private static bool SatisfiesSpecial(SpecialConstraints required, TypeShape shape)
    {
        if (required == SpecialConstraints.None)
        {
            return true;
        }

        if ((required & SpecialConstraints.ReferenceType) != 0 && !shape.IsReferenceType)
        {
            return false;
        }

        if ((required & SpecialConstraints.ValueType) != 0 && !shape.IsValueType)
        {
            return false;
        }

        if ((required & SpecialConstraints.Unmanaged) != 0 && !shape.IsUnmanaged)
        {
            return false;
        }

        if ((required & SpecialConstraints.NotNull) != 0 && !shape.IsNotNull)
        {
            return false;
        }

        if ((required & SpecialConstraints.Constructor) != 0 && !shape.HasParameterlessCtor)
        {
            return false;
        }

        return true;
    }
}
