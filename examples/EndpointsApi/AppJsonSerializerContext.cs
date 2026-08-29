using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.EndpointsApi.Features.Ops;
using UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

namespace UnambitiousFx.Examples.EndpointsApi;

/// <summary>
///     Source-generated JSON metadata for every type reachable from an endpoint's request/response
///     or the OpenAPI document. Required for Native AOT, where reflection-based serialization is
///     unavailable. <see cref="ProblemDetails" /> and <see cref="HttpValidationProblemDetails" /> are
///     included even though the endpoints generator's SYNE008 diagnostic does not check for them —
///     they are still needed at runtime for the OpenAPI document and error responses.
/// </summary>
/// <remarks>
///     <see cref="IAsyncEnumerable{T}" /> of <see cref="TaskDto" /> is registered even though
///     <c>StreamTasksEndpoint</c>'s bound item type (<c>TaskDto</c>) is already covered above. SYNE008
///     resolves a <c>StreamEndpoint&lt;TRequest, TItem&gt;</c>'s JSON-relevant response type as bare
///     <c>TItem</c>, not <c>IAsyncEnumerable&lt;TItem&gt;</c> — the type
///     <c>StreamEndpoint.CreateDescriptor</c> actually declares via
///     <c>ProducesResponseMetadata(..., typeof(IAsyncEnumerable&lt;TItem&gt;), ...)</c> and the type
///     <c>Microsoft.AspNetCore.OpenApi</c> asks the resolver chain for when building the OpenAPI
///     schema for <c>GET /tasks/stream</c>. Without this registration the build stays warning-free
///     (SYNE008 never flags it — a false negative) but <c>/openapi/v1.json</c> throws
///     <c>NotSupportedException: JsonTypeInfo metadata for type
///     'IAsyncEnumerable&lt;TaskDto&gt;' was not provided</c> at request time. Confirmed by removing
///     this line and re-running the app: build stays green, <c>/openapi/v1.json</c> 500s.
/// </remarks>
[JsonSerializable(typeof(TaskDto))]
[JsonSerializable(typeof(IReadOnlyList<TaskDto>))]
[JsonSerializable(typeof(IAsyncEnumerable<TaskDto>))]
[JsonSerializable(typeof(TaskCreated))]
[JsonSerializable(typeof(CreateTaskCommand))]
[JsonSerializable(typeof(UpdateTaskCommand))]
[JsonSerializable(typeof(DeleteTaskCommand))]
[JsonSerializable(typeof(GetTaskQuery))]
[JsonSerializable(typeof(ListTasksQuery))]
[JsonSerializable(typeof(StreamTasksQuery))]
[JsonSerializable(typeof(HealthDto))]
[JsonSerializable(typeof(TagReportDto))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;
