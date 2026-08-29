# EndpointsApi

A Native AOT ASP.NET Core app that carries one live instance of every shape
`UnambitiousFx.Synapse.Endpoints` can express. It is a worked example first and a regression harness
second: it is the target of the CI Native AOT smoke test, and `../EndpointsApi.Tests` drives every
route below through the real pipeline.

Run it with `dotnet run`, then read `/openapi/v1.json`.

## Shape → file

### High level — the analyzer writes the binding

| Base class | Route | File |
|---|---|---|
| `Endpoint<TRequest, TResponse>` | `GET /tasks`, `GET /tasks/{taskId}`, `POST /tasks` | `Features/Tasks/Endpoints.cs` |
| `Endpoint<TRequest>` | `PUT /tasks/{taskId}`, `DELETE /tasks/{taskId}` | `Features/Tasks/Endpoints.cs` |
| `StreamEndpoint<TRequest, TItem>` | `GET /tasks/stream` | `Features/Tasks/Endpoints.cs` |
| `StreamEndpoint<TRequest, TItem>`, bound from a body | `POST /tasks/stream/search` | `Features/Tasks/StreamSearch.cs` |
| `MappedEndpoint<THttpRequest, TRequest, TResponse, THttpResponse>` | `POST /v1/tasks` | `Features/Contracts/CreateTaskV1.cs` |

### Low level — the binding is yours

| Base class | Route | File |
|---|---|---|
| `RawEndpoint` | `GET /health` | `Features/Ops/RawEndpoints.cs` |
| `RawEndpoint<TRequest, TResponse>` | `GET /reports` | `Features/Ops/RawEndpoints.cs` |
| `RawEndpoint<TRequest>` | `DELETE /ops/tasks` | `Features/Ops/PurgeTasks.cs` |

### Routing

| Shape | Route | File |
|---|---|---|
| Groups — shared prefix and tag | every `/tasks` route | `Features/Tasks/TasksGroup.cs` |
| A route computed in `Configure` | `GET /tasks/search` | `Features/Tasks/Endpoints.cs` |
| `[HttpEndpoint(method, route)]` for a verb with no attribute | `HEAD /tasks` | `Features/Tasks/HeadTasksEndpoint.cs` |
| `[Patch]` | `PATCH /tasks/{taskId}` | `Features/Tasks/PatchTask.cs` |

### Binding

| Rule | Where to see it | File |
|---|---|---|
| 1 — `[NotBound]`, and why it needs `[JsonIgnore]` beside it | `PatchTaskCommand.StampedAt` | `Features/Tasks/PatchTask.cs` |
| 2 — `[FromHeader]` | `PatchTaskCommand.Actor` | `Features/Tasks/PatchTask.cs` |
| 2 — `[FromQuery]` | `SearchTasksQuery.Title` | `Features/Tasks/Messages.cs` |
| 3 — route parameter by name | `GetTaskQuery.TaskId` | `Features/Tasks/Messages.cs` |
| 4 — query string on a bodyless verb | `SearchTasksQuery` | `Features/Tasks/Messages.cs` |
| 5 — the request body | `CreateTaskCommand.Title` | `Features/Tasks/Messages.cs` |
| Hand-written binding, one repeated query key → a collection | `TagReportEndpoint` | `Features/Ops/RawEndpoints.cs` |
| Hand-written binding, one header split → a collection | `PurgeTasksEndpoint` | `Features/Ops/PurgeTasks.cs` |
| Accumulating validation — one `400` listing every bad input | `TagReportEndpoint` | `Features/Ops/RawEndpoints.cs` |

### Responses

| Mapper | Route | File |
|---|---|---|
| `Created(location)` → `201` | `POST /tasks` | `Features/Tasks/Endpoints.cs` |
| `Accepted(location)` → `202` | `POST /tasks/{taskId}/archive` | `Features/Tasks/TaskLifecycle.cs` |
| `NoContent()` → `204`, discarding a value the handler did return | `PUT /tasks/{taskId}/title` | `Features/Tasks/TaskLifecycle.cs` |
| `StatusCode(int)` → any code with no body | `POST /tasks/compact` | `Features/Tasks/TaskLifecycle.cs` |
| `Ok()` → `200`, the default stated explicitly | `POST /v1/tasks` | `Features/Contracts/CreateTaskV1.cs` |
| `Produces<T>()` / `Produces(int)` on a hand-written handler | `GET /health` | `Features/Ops/RawEndpoints.cs` |

### Cross-cutting

| Shape | File |
|---|---|
| A pipeline behaviour rewriting the message on its way to the handler | `Features/Tasks/PatchTask.cs` |
| Source-generated JSON metadata for Native AOT | `AppJsonSerializerContext.cs` |

## Things this example exists to pin

Several of these routes are here because the shape was once broken, and the XML comments say so at
each site. Worth knowing before writing your own:

- A `required` property bound from the route is fine on a **bodyless** verb and wrong on a
  body-carrying one — the message is deserialized before the route value is applied, so `required`
  makes the deserializer demand a field the payload never carries. Compare `GetTaskQuery.TaskId`
  with `UpdateTaskCommand.TaskId` and `ArchiveTaskCommand.TaskId`.
- `[NotBound]` alone does not stop a caller supplying a value on a body-carrying verb. Pair it with
  `[JsonIgnore]`, and have the pipeline overwrite unconditionally. See `PatchTaskCommand.StampedAt`.
- `POST` and `PUT` read a JSON body even when every property binds from the route, so a caller has
  to send `{}` — sending nothing is a `400`. See `ArchiveTaskEndpoint`.
- A route declared only in `Configure` leaves the generator with no verb to reason about, so it
  assumes a bodyless one; annotate the properties explicitly. See `SearchTasksEndpoint`.
- `POST /tasks/stream/search` binds its message from the body but publishes no `requestBody` in the
  OpenAPI document — `StreamEndpoint` is the one body-carrying tier that does not declare
  `Accepts<TRequest>`. Pinned by `StreamSearchTests.GetOpenApi_DoesNotYetDocumentThePostStreamRequestBody`.
