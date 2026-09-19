namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Associates an endpoint with an <see cref="EndpointGroup" />, whose prefix, tags and
///     policies are applied to it.
/// </summary>
/// <typeparam name="TGroup">The group type.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InGroupAttribute<TGroup> : Attribute
    where TGroup : EndpointGroup, new()
{
}
