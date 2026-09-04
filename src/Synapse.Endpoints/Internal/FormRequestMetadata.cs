using Microsoft.AspNetCore.Http.Metadata;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Declares that an endpoint accepts a form body, without describing its schema.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="RequestType" /> is deliberately <see langword="null" />. The content types are
///         what <c>ConsumesMatcherPolicy</c> needs in order to answer <c>415</c> rather than letting a
///         JSON request through to a <c>400</c>; the schema is a separate, larger piece of work (see
///         <c>docs/endpoints/features/018-openapi-parameter-metadata.md</c>). Passing the message type
///         here instead would hand the schema generator a type containing <c>IFormFile</c> — an
///         interface over a buffered stream, which is not describable that way — so declaring no
///         schema is the honest answer rather than a wrong one.
///     </para>
///     <para>
///         A custom metadata type for the same reason <c>ProducesResponseMetadata</c> is one: the
///         framework's own <c>Accepts</c> helpers cannot express this shape.
///     </para>
/// </remarks>
internal sealed class FormRequestMetadata : IAcceptsMetadata
{
    internal static readonly FormRequestMetadata Instance = new();

    private FormRequestMetadata()
    {
    }

    public Type? RequestType => null;

    public IReadOnlyList<string> ContentTypes { get; } =
        ["multipart/form-data", "application/x-www-form-urlencoded"];

    public bool IsOptional => false;
}
