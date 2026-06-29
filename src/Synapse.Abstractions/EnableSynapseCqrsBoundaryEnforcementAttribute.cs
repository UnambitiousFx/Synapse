namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Opts the assembly into CQRS boundary enforcement. When applied, the Synapse source generator
///     emits closed-generic <c>CqrsBoundaryEnforcementBehavior&lt;TRequest, TResponse&gt;</c> registrations
///     (one per discovered request handler) at the outermost position of the pipeline, instead of a single
///     open-generic registration.
/// </summary>
/// <remarks>
///     This is the AOT-safe replacement for the runtime <c>cfg.EnableCqrsBoundaryEnforcement()</c> call.
///     Open-generic pipeline behaviors cannot be closed over value-type responses (e.g. <c>Guid</c>,
///     <c>int</c>) under Native AOT, so enforcement must be expressed as generator-emitted closed
///     registrations the generator can synthesize ahead of time.
///     <para>
///         Applied at the composition root, this covers request handlers declared in referenced assemblies
///         too — the root's generator discovers them across the assembly reference and emits their closed
///         enforcement registrations. Sub-projects therefore need not repeat the attribute. Leaving it on a
///         referenced library is still safe: duplicate enforcement registrations are deduplicated at the
///         service-collection level. Apply <c>[assembly: DisableSynapseCrossAssemblyBehaviors]</c> to restrict
///         a root's enforcement (and its open-generic behaviors) to same-assembly handlers.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
[Obsolete(
    "Use [assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<>))] and " +
    "[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<,>))] instead. " +
    "This attribute remains a working alias for the same registrations.")]
public sealed class EnableSynapseCqrsBoundaryEnforcementAttribute : Attribute;
