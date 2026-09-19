# 003 — Endpoint test harness

|  |  |
|---|---|
| **Status** | ✅ Shipped |
| **Priority** | High |
| **Area** | Testing |
| **Tiers** | All |
| **Breaking** | No — additive, new package or test-only namespace |

## Problem

An endpoint cannot be exercised without booting a host. `BindAsync` and `HandleAsync` are public,
which makes calling them the obvious thing to try, and doing so throws by design.

This contradicts the repository's own guidance — *"test handlers in isolation + integration tests
for end-to-end dispatch"* (`.claude/CLAUDE.md`) — for the one component that has no isolation story.

## State before this change

- `src/Synapse.Endpoints/EndpointRouteBuilderExtensions.cs` — `MapEndpoint<TEndpoint>` is the only
  path that calls `CreateDescriptor`, which is the only path that calls `CreatePlan`, which is what
  populates `_binder` and `_configuration`.
- `src/Synapse.Endpoints/SynapseEndpoint.cs` — `Mapped<TState>` throws
  `InvalidOperationException("… has not been mapped …")` when either field is still null. Good
  diagnostics (see `docs/known-issues/056`), but it tells the user to go and build a pipeline.
- `test/Synapse.Endpoints.Tests` works around this with its own scaffolding
  (`ScaffoldTests.cs`); that scaffolding is not shipped, so consumers cannot reuse it.

## What you cannot write today

The obvious unit test — both members it calls are public:

```csharp
[Fact]
public async Task GetTask_WithUnknownId_Returns404()
{
    // Arrange
    var endpoint = new GetTaskEndpoint();
    var context  = new DefaultHttpContext();
    context.Request.RouteValues["taskId"] = Guid.NewGuid().ToString();

    // Act
    var result = await endpoint.HandleAsync(context, CancellationToken.None);

    // System.InvalidOperationException: Endpoint 'GetTaskEndpoint' has not been mapped, so it has
    // no request-time state. That state is created by MapEndpoint<TEndpoint>() (or
    // MapSynapseEndpoints()) at startup, which means HandleAsync and BindAsync cannot run before
    // the endpoint is mapped. […] map the endpoint into a route builder and exercise it through
    // the pipeline instead.
}
```

The message is accurate and the failure is deliberate (`docs/known-issues/056`), but the only route
it leaves open is a host:

```csharp
public sealed class GetTaskTests : IClassFixture<WebApplicationFactory<Program>>
{
    // Every dependency the whole application registers must resolve before this can assert that
    // one endpoint answers 404 for an unknown id.
    private readonly WebApplicationFactory<Program> _factory;

    [Fact]
    public async Task GetTask_WithUnknownId_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync($"/tasks/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
```

That is the right shape for `examples/EndpointsApi.Tests`, which is deliberately end-to-end. It is
the wrong shape as the *only* option — and it is unavailable to a library that ships endpoints
without a `Program` to boot.

## Proposed API

A test-only entry point that runs the startup half of the lifecycle and hands back an instance ready
to invoke:

```csharp
var endpoint = EndpointHarness.Create<CreateTaskEndpoint>(options =>
{
    options.Services = services;                 // for context.Service<T>() and IHttpInvoker
    options.RouteValues["taskId"] = taskId;      // optional seeded HttpContext
});

var result = await endpoint.HandleAsync(context, CancellationToken.None);
result.ShouldBe().Status(201);
```

Plus a request builder so the common case is one expression:

```csharp
var response = await EndpointHarness.Create<GetTaskEndpoint>()
    .Get("/tasks/{taskId}", new { taskId })
    .SendAsync();
```

Ships as `UnambitiousFx.Synapse.Endpoints.Testing` so the runtime package stays free of test
dependencies.

### With the proposal

The test that was impossible above, at unit scale:

```csharp
[Fact]
public async Task GetTask_WithUnknownId_Returns404()
{
    // Arrange
    var harness = EndpointHarness.Create<GetTaskEndpoint>(options =>
        options.Services.AddSingleton<ITaskRepository>(new EmptyTaskRepository()));

    // Act
    var response = await harness.Get("/tasks/{taskId}", new { taskId = Guid.NewGuid() })
                                .SendAsync();

    // Assert
    response.ShouldBe().Status(404);
}
```

And the binding half, which today cannot be tested at all without a request going over a socket:

```csharp
[Fact]
public async Task GetTask_WithNonGuidId_Returns400NamingTheField()
{
    var response = await EndpointHarness.Create<GetTaskEndpoint>()
        .Get("/tasks/{taskId}", new { taskId = "not-a-guid" })
        .SendAsync();

    response.ShouldBe().ValidationProblem().WithError("taskId", "The route value is not a valid Guid.");
}
```

## Acceptance criteria

- [x] `CreatePlan` runs, binder and configuration resolve, `Mapped<T>` does not throw.
- [x] Works for all five tiers, including `StreamEndpoint` (harness materialises the stream).
- [x] Binding failures surface as the same `400` `HttpValidationProblemDetails` the pipeline writes.
- [x] A fake `IInvoker` is supplied by default so an endpoint can be tested without registering
      a real handler, while the real `IHttpInvoker` and `DefaultFailureHttpMapper` stay in the
      pipeline.
- [x] Documented on a new `docs/docs/endpoints/reference/testing.mdx` page.

## State after this change

The shipped design departs from the proposal above it in three places:

- **The request is addressed by a concrete URL, not a template plus an anonymous object.** The
  proposal's `harness.Get("/tasks/{taskId}", new { taskId })` implied the harness would substitute
  route values itself. The shipped `EndpointRequest` takes a plain URL —
  `harness.Get($"/tasks/{taskId}")` — and hands it straight to the real route matcher, so the
  endpoint's own route is what gets exercised: constraints, group prefixes and verb matching all
  apply exactly as they do in an application, rather than being bypassed by pre-substituted values.

- **The fake is `IInvoker`, not `IHttpInvoker`.** The proposal's `options.Services = services; //
  for context.Service<T>() and IHttpInvoker` implied `IHttpInvoker` itself would be replaced. It
  is not: the harness fakes `IInvoker`, the mediator seam a stubbed handler plugs into, and leaves
  the real `IHttpInvoker` and the real `DefaultFailureHttpMapper` in the pipeline. A stubbed failed
  `Result` is therefore mapped to HTTP by the same code the application runs, so a `404` in a test
  is the same `404` the application writes — not a status the harness invented on the fake's
  behalf.

- **An unmatched URL throws rather than returning routing's own `404`.** Returning that `404` like
  any other response would let a typo'd URL silently satisfy an assertion written for the
  endpoint's *own* `404` — the one produced by `Result.FailNotFound(...)` and mapped by
  `DefaultFailureHttpMapper`. `SendAsync` instead throws `InvalidOperationException`, naming the
  mapped route, whenever the response is a `404` with no matched endpoint at all.

`SynapseEndpoint.Mapped<TState>`'s error message now points at the harness, as this document's original
Notes section asked for — see `src/Synapse.Endpoints/SynapseEndpoint.cs`.
