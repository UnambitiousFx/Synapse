namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Binds a property from a request header. Headers are never bound by convention, so this
///     attribute is the only way to read one into a message.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class FromHeaderAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="FromHeaderAttribute" /> class.</summary>
    /// <param name="name">The header name. Defaults to the property name when omitted.</param>
    public FromHeaderAttribute(string? name = null)
    {
        Name = name;
    }

    /// <summary>Gets the header name, or <see langword="null" /> to use the property name.</summary>
    public string? Name { get; }
}
