namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Registers an open-generic pipeline behavior globally — applied to every matching handler in the
///     compilation — without decorating the behavior class itself. The Synapse source generator closes the
///     behavior over each discovered handler and emits one closed (AOT-safe) registration per match, exactly
///     as it does for a <c>[PipelineBehavior]</c>-decorated open-generic class.
/// </summary>
/// <remarks>
///     This is the opt-in-at-the-composition-root mechanism for behaviors the consumer does not own — for
///     example a behavior shipped in a referenced NuGet package, which cannot be decorated with
///     <c>[PipelineBehavior]</c> at the source. Pass the <em>unbound</em> generic definition:
///     <c>typeof(LoggingBehavior&lt;,&gt;)</c>. A non-generic <c>typeof</c> is the only way to hand an
///     open generic to an attribute (an unbound generic cannot be a generic type argument), so this attribute
///     is intentionally non-generic.
///     <para>
///         The behavior type must be <c>public</c> (the generator emits its name into generated code) and must
///         implement one of the Synapse pipeline interfaces — <c>IRequestPipelineBehavior&lt;&gt;</c>,
///         <c>IRequestPipelineBehavior&lt;,&gt;</c>, <c>IEventPipelineBehavior&lt;&gt;</c>, or
///         <c>IStreamRequestPipelineBehavior&lt;,&gt;</c>. Pipeline position is controlled by the behavior
///         implementing <c>IOrderedPipelineBehavior</c>, not by this attribute.
///     </para>
///     <para>
///         Open-generic constraints (named-type and <c>class</c>/<c>struct</c>/<c>unmanaged</c>/<c>notnull</c>/
///         <c>new()</c>) are honoured: a behavior is only closed over handlers whose request/response types
///         satisfy them. Like other generator-emitted behaviors, this covers handlers declared in referenced
///         assemblies too; apply <c>[assembly: DisableSynapseCrossAssemblyBehaviors]</c> to restrict it to
///         same-assembly handlers. Duplicate registrations across opted-in assemblies are deduplicated at the
///         service-collection level.
///     </para>
/// </remarks>
/// <param name="behaviorType">
///     The unbound open-generic behavior definition, e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>.
/// </param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class SynapseGlobalBehaviorAttribute(Type behaviorType) : Attribute
{
    /// <summary>The open-generic behavior type to register globally.</summary>
    public Type BehaviorType { get; } = behaviorType;
}
