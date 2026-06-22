namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Describes a class decorated with <c>[Validator]</c> discovered by the source generator. The request
///     type is read from the implemented <c>IRequestValidator&lt;TRequest&gt;</c> interface; the response type
///     (when the request implements <c>IRequest&lt;TResponse&gt;</c>) selects the with-response validation
///     behavior arity.
/// </summary>
public readonly record struct ValidatorDetail
{
    public ValidatorDetail(string className,
        string @namespace,
        string fullyQualifiedName,
        string fullRequestTypeName,
        string? fullResponseType)
    {
        ClassName = className;
        Namespace = @namespace;
        FullyQualifiedName = fullyQualifiedName;
        FullRequestTypeName = fullRequestTypeName;
        FullResponseType = fullResponseType;
    }

    /// <summary>Simple class name (without namespace).</summary>
    public string ClassName { get; }

    /// <summary>Namespace of the validator class.</summary>
    public string Namespace { get; }

    /// <summary>
    ///     Fully-qualified type name (including <c>global::</c> prefix and any enclosing types) of the validator
    ///     class, with generic type parameters omitted. Used to emit a compilable registration even for nested
    ///     validator classes.
    /// </summary>
    public string FullyQualifiedName { get; }

    /// <summary>Fully-qualified validated request type.</summary>
    public string FullRequestTypeName { get; }

    /// <summary>
    ///     Fully-qualified response type when the request implements <c>IRequest&lt;TResponse&gt;</c>; null for
    ///     no-response requests.
    /// </summary>
    public string? FullResponseType { get; }

    public string FullValidatorTypeName => FullyQualifiedName;
}

/// <summary>
///     Result of scanning a single <c>[Validator]</c>-decorated class. <see cref="Detail" /> is null when the
///     scan produced no emittable validator: either the class implements no
///     <c>IRequestValidator&lt;TRequest&gt;</c> interface (reported via diagnostic MDG009), or the validated
///     request implements multiple <c>IRequest&lt;TResponse&gt;</c> so the response type is ambiguous
///     (<see cref="AmbiguousResponseRequest" /> set, reported via diagnostic MDG011).
/// </summary>
public readonly record struct ValidatorScan
{
    public ValidatorScan(LocationInfo? location, ValidatorDetail? detail, string? ambiguousResponseRequest = null)
    {
        Location = location;
        Detail = detail;
        AmbiguousResponseRequest = ambiguousResponseRequest;
    }

    public LocationInfo? Location { get; }

    public ValidatorDetail? Detail { get; }

    /// <summary>
    ///     When <see cref="Detail" /> is null because the validated request implements more than one
    ///     <c>IRequest&lt;TResponse&gt;</c>, holds that request's display name for the MDG011 diagnostic message;
    ///     null when the null detail instead means no validator interface was implemented (MDG009).
    /// </summary>
    public string? AmbiguousResponseRequest { get; }
}
