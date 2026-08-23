using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Base type shared by every endpoint. Because its only abstract member is internal, endpoints
///     must derive from one of the library's own base classes rather than from this type directly.
/// </summary>
public abstract class EndpointBase
{
    /// <summary>
    ///     Builds the non-generic descriptor used to map this endpoint. Called once at startup.
    /// </summary>
    internal abstract EndpointDescriptor CreateDescriptor(EndpointMetadata metadata);
}
