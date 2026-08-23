using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Non-generic description of one mapped endpoint. Deliberately non-generic: the single
///     <c>Map*</c> call site must not sit inside a generic method, or the ASP.NET Request Delegate
///     Generator reports RDG011 and falls back to reflection-based binding.
/// </summary>
internal sealed class EndpointDescriptor
{
    public required string Route { get; init; }

    public required string[] HttpMethods { get; init; }

    public required Func<HttpContext, Task> InvokeAsync { get; init; }

    public required Action<RouteHandlerBuilder> ApplyMetadata { get; init; }
}
