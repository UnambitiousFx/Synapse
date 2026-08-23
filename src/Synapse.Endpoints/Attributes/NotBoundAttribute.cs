namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Excludes a property from binding entirely. Use it for values a pipeline behaviour or
///     handler sets, so a caller cannot supply them by guessing the property name.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class NotBoundAttribute : Attribute
{
}
