namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>One field or file part of a form-bound message, for the request-body schema.</summary>
/// <remarks>
///     Separate from <see cref="BoundParameterMetadata" /> because a form field is a request-body
///     concern, not a parameter: it is rendered into the <c>multipart/form-data</c> and
///     <c>application/x-www-form-urlencoded</c> schemas rather than into the operation's
///     <c>parameters</c> array.
/// </remarks>
public sealed class FormFieldMetadata
{
    /// <summary>The form field name the binder reads.</summary>
    public required string Name { get; init; }

    /// <summary>Whether a request omitting this field is rejected by the binder.</summary>
    public required bool Required { get; init; }

    /// <summary>Whether the field repeats, so its schema is an array.</summary>
    public required bool IsArray { get; init; }

    /// <summary>
    ///     The scalar or element type; <c>IFormFile</c> for a file part, which renders as
    ///     <c>type: string, format: binary</c>.
    /// </summary>
    public required Type ValueType { get; init; }
}
