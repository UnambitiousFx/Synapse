# IPipelineDescriber — design

Sub-issue #101 of #96. Lets users inspect the resolved pipeline of a request or event type, so an architecture test
can assert that security behaviors are never skipped.

## Goals

- Report, for a message type, the handler(s) and the behaviors in the order they execute.
- The report is the chain the dispatcher actually runs, not a second computation of it.
- Native-AOT safe for the generic API.

## Non-goals

- Streams (`IStreamRequest`) — a later slice.
- Exposing behavior instances.
- Validation of the configuration — that is #102, which will build on this.

## Public API (`Synapse.Abstractions`)

```csharp
public interface IPipelineDescriber
{
    PipelineDescription? Describe<TRequest>() where TRequest : IRequest;
    PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse> where TResponse : notnull;
    PipelineDescription? DescribeEvent<TEvent>() where TEvent : class, IEvent;

    [RequiresDynamicCode(...)] PipelineDescription? Describe(Type requestType);
    [RequiresDynamicCode(...)] PipelineDescription? DescribeEvent(Type eventType);
}

public sealed record PipelineDescription(IReadOnlyList<Type> Handlers, IReadOnlyList<BehaviorDescription> Behaviors);
public sealed record BehaviorDescription(Type Type, uint Order);
```

- `Behaviors` is outermost first (lowest `Order` first, ties in registration order).
- `Handlers` has one entry for a request, one per subscriber for an event.
- Returns `null` when nothing handles the message. No instances are exposed.
- `Describe(Type)` accepts a type implementing `IRequest` or `IRequest<TResponse>` and picks the overload.

## Implementation (`Synapse`)

- Approach: ask the component that runs the pipeline (approach C in the brainstorm). `ProxyRequestHandler<,>` and
  `ProxyRequestHandler<,,>` implement an internal `IPipelineInfo` returning the handler type and their already-sorted
  behaviors. No change to the dispatch path; the proxy already holds the sorted array.
- Events: `EventDispatcher.BuildPipeline` is split so a single private method resolves the handlers and the sorted
  behaviors (`PipelineBehaviorOrdering.OrderOf`), used by both building and describing.
- `PipelineDescriber` is a singleton registered with `TryAdd` in `AddSynapse`. Each call opens a scope from
  `IServiceScopeFactory`, resolves, builds the description, disposes the scope.
- `Describe(Type)` / `DescribeEvent(Type)` use `MakeGenericMethod` over the generic methods.

## Caveats to document

- Describing instantiates the behaviors, because `Order` is an instance property. A behavior whose constructor needs
  something that only exists in a real request could throw in the describer's scope.
- `Describe(Type)` is not Native-AOT safe; it is meant for tests.

## Testing

- Describer output equals the observed execution order of a real dispatch (guards against drift).
- Ties keep registration order; `null` for an unhandled request; several event handlers; behaviors wired through
  `RegisterRequestPipelineBehavior` and through a global behavior; `Describe(Type)` for both request shapes and an
  event; a non-request type throws `ArgumentException`.
- `examples/MinimalApi` calls the generic methods so the Native AOT CI job exercises them at runtime.

## Docs

"Architecture tests" section in `pipelines.mdx` with the "every request traverses the security chain" example, and the
two caveats above.
