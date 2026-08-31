using Microsoft.AspNetCore.Routing;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Maps every endpoint declared in one assembly. Implemented by the generated
///     <c>EndpointGroup</c> class, which is a plain sequence of <c>MapEndpoint&lt;T&gt;()</c> calls.
/// </summary>
public interface IEndpointGroup
{
    /// <summary>Maps this assembly's endpoints.</summary>
    /// <param name="endpoints">The route builder.</param>
    void Map(IEndpointRouteBuilder endpoints);
}
