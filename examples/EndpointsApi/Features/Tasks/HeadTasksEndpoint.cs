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
///         It reuses <see cref="ListTasksQuery" /> and therefore <c>ListTasksEndpoint</c>'s generated
///         binder: only one binder is emitted per bound type, and this endpoint resolves to the same
///         bindings as that one (bodyless verb, no route parameters, no properties), so sharing it
///         changes nothing. Were the two to resolve differently, SYNE013 would say so.
///     </para>
/// </remarks>
[HttpEndpoint("HEAD", "/")]
[InGroup<TasksGroup>]
public sealed class HeadTasksEndpoint : Endpoint<ListTasksQuery, IReadOnlyList<TaskDto>>;
