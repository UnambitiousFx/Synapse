using Microsoft.AspNetCore.Http.Metadata;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Declares that an endpoint accepts a form body, and describes its fields.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="RequestType" /> is deliberately <see langword="null" />. The content types are
///         what <c>ConsumesMatcherPolicy</c> needs in order to answer <c>415</c> rather than letting a
///         JSON request through to a <c>400</c>; passing the message type here instead would hand the
///         schema generator a type containing <c>IFormFile</c> — an interface over a buffered stream,
///         which is not describable that way. <see cref="Fields" /> is how the schema is described
///         instead: field by field, by a consumer that knows how to render one.
///     </para>
///     <para>
///         A custom metadata type for the same reason <c>ProducesResponseMetadata</c> is one: the
///         framework's own <c>Accepts</c> helpers cannot express this shape.
///     </para>
///     <para>
///         <see langword="public" /> rather than <see langword="internal" /> because
///         <c>UnambitiousFx.Synapse.Endpoints.OpenApi</c> reads <see cref="Fields" />, and
///         <c>InternalsVisibleTo</c> covers only this assembly's own test project. No singleton
///         instance, unlike before: the field list is per message type.
///     </para>
/// </remarks>
public sealed class FormRequestMetadata : IAcceptsMetadata
{
    /// <summary>Initializes a new instance of the <see cref="FormRequestMetadata" /> class.</summary>
    /// <param name="fields">The form fields and file parts the binder reads; may be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fields" /> is <see langword="null" />.</exception>
    public FormRequestMetadata(IReadOnlyList<FormFieldMetadata> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = fields;
    }

    /// <summary>Gets the form fields and file parts, for the request-body schema.</summary>
    public IReadOnlyList<FormFieldMetadata> Fields { get; }

    /// <inheritdoc />
    public Type? RequestType => null;

    /// <inheritdoc />
    public IReadOnlyList<string> ContentTypes { get; } =
        ["multipart/form-data", "application/x-www-form-urlencoded"];

    /// <inheritdoc />
    public bool IsOptional => false;
}
