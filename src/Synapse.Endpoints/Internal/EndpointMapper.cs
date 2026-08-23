using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Holds the only <c>Map*</c> invocation in the library.
/// </summary>
internal static class EndpointMapper
{
    /// <summary>
    ///     Maps one descriptor onto the route table.
    /// </summary>
    /// <remarks>
    ///     Three details are load-bearing and must not be "simplified":
    ///     <list type="number">
    ///         <item>
    ///             This method is NOT generic. A type parameter at a <c>Map*</c> call site produces
    ///             RDG011 unconditionally, which silently degrades to reflection-based binding and
    ///             throws at request time under Native AOT.
    ///         </item>
    ///         <item>
    ///             The lambda is cast to <see cref="Delegate" />. Without the cast it binds to the
    ///             <c>RequestDelegate</c> overload, which adds no <c>MethodInfo</c> to endpoint
    ///             metadata — and without a <c>MethodInfo</c> the endpoint is silently absent from
    ///             the OpenAPI document.
    ///         </item>
    ///         <item>
    ///             The lambda is written literally here. A <c>Delegate</c>-typed variable produces
    ///             RDG002 and the same reflection fallback.
    ///         </item>
    ///     </list>
    /// </remarks>
    internal static RouteHandlerBuilder Map(IEndpointRouteBuilder endpoints,
        EndpointDescriptor descriptor)
    {
        var builder = endpoints.MapMethods(
            descriptor.Route,
            descriptor.HttpMethods,
            (Delegate)(async (HttpContext context) => await descriptor.InvokeAsync(context)));

        descriptor.ApplyMetadata(builder);
        return builder;
    }
}
