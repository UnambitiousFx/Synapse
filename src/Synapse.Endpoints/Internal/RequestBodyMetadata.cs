using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Applies the request-body metadata for a resolved <see cref="RequestBodyKind" />.
/// </summary>
/// <remarks>
///     One place rather than six. Every bound tier used to spell out the same
///     <c>Accepts&lt;TRequest&gt;("application/json")</c> call, which meant adding a second content
///     type would have been six near-identical edits and six chances to disagree.
/// </remarks>
internal static class RequestBodyMetadata
{
    internal static void Apply(RouteHandlerBuilder builder,
        RequestBodyKind kind,
        Type requestType,
        IReadOnlyList<FormFieldMetadata> formFields)
    {
        switch (kind)
        {
            case RequestBodyKind.Json:
                builder.Accepts(requestType, "application/json");
                break;
            case RequestBodyKind.Form:
                builder.WithMetadata(new FormRequestMetadata(formFields));
                break;
        }
    }
}
