namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Binds a property from the request form — <c>multipart/form-data</c> or
///     <c>application/x-www-form-urlencoded</c>.
/// </summary>
/// <remarks>
///     Applying it to any property makes the whole message form-bound: a request is a form or it is
///     JSON, never both, so the properties that would otherwise have come from a JSON body come from
///     the form instead. A property typed <c>IFormFile</c> has the same effect without the attribute,
///     since a file has only one place it could come from.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class FromFormAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="FromFormAttribute" /> class.</summary>
    /// <param name="name">The form field name. Defaults to the property name when omitted.</param>
    public FromFormAttribute(string? name = null)
    {
        Name = name;
    }

    /// <summary>Gets the form field name, or <see langword="null" /> to use the property name.</summary>
    public string? Name { get; }
}
