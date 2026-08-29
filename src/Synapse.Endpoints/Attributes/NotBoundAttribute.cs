namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Excludes a property from the bindings the analyzer generates. Use it for values a pipeline
///     behaviour or handler sets rather than the caller.
/// </summary>
/// <remarks>
///     <para>
///         This excludes the property from the route, query and header assignments the generated
///         binder emits — rule 1 of the five binding rules, which wins over every other rule.
///     </para>
///     <para>
///         It does not, and cannot, exclude the property from JSON deserialization. On a verb that
///         carries a body the binder populates the whole message in one shot through
///         <c>System.Text.Json</c>, which does not read this attribute, so a caller who names the
///         property in the payload still sets it. Pair it with <c>[JsonIgnore]</c> there, and have
///         whatever owns the value assign it unconditionally rather than only when it is still at its
///         default. <c>SYNE015</c> reports the shape that needs this. On a bodyless verb nothing
///         deserializes the message and this attribute alone is sufficient.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class NotBoundAttribute : Attribute
{
}
