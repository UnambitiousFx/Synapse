using UnambitiousFx.Synapse.Endpoints;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>
///     A verb with no attribute of its own, declared through the general
///     <see cref="HttpEndpointAttribute" />.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Get]</c>, <c>[Post]</c>, <c>[Put]</c>, <c>[Patch]</c> and <c>[Delete]</c> are just
///         subclasses of <c>[HttpEndpoint]</c>; anything else — <c>HEAD</c>, <c>OPTIONS</c>,
///         <c>TRACE</c> — uses the base attribute directly.
///     </para>
///     <para>
///         It reuses <see cref="ListTasksQuery" />, and gets its own generated binding for it: the
///         binding is emitted per endpoint, resolved from this endpoint's own route and verb, so two
///         endpoints sharing a message cannot resolve it for each other.
///     </para>
/// </remarks>
[HttpEndpoint("HEAD", "/")]
[InGroup<TasksGroup>]
public sealed partial class HeadTasksEndpoint : Endpoint<ListTasksQuery, IReadOnlyList<TaskDto>>;
