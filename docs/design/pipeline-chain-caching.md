# Design: Pipeline chain composition caching (#2 — future work)

> Status: **proposed, not implemented.** Plain markdown, intentionally outside the docusaurus sidebar.
> Companion to the open-generic constraint fix (#1, already shipped). Implement after #1.

## Problem

Every request/event dispatch recomposes the behavior chain and allocates one closure per behavior.

`ProxyRequestHandler.ExecutePipelineAsync` (`src/Synapse/ProxyRequestHandler.cs:24`) drives the
pipeline by recursion:

```csharp
return _behaviors[index].HandleAsync(request, Next, cancellationToken);

ValueTask<Result> Next(TRequest inRequest, CancellationToken inCancellationToken)
{
    return ExecutePipelineAsync(inRequest, index + 1, inCancellationToken);
}
```

`Next` is a local function captured into a delegate. Because it captures `this` **and the changing
`index`**, the compiler emits a fresh display-class instance at every recursion level — i.e. **one heap
allocation per behavior, per dispatch**. With N behaviors that is N allocations on the hot path each
time a request flows through.

Compounding it: the proxy and the behaviors are registered **`Scoped`**
(`src/Synapse/DependencyInjectionExtensions.cs:70,84`), so a new proxy is constructed and the behavior
array re-sorted (`OrderBy(PipelineBehaviorOrdering.OrderOf)`) for every scope.

The same shape exists in:
- `src/Synapse/ProxyStreamRequestHandler.cs` (async-enumerable variant).
- `src/Synapse/Publish/EventDispatcher.cs` — already caches the *sorted behavior array* per type
  (`_behaviorCache`, `:25`) but still recurses with per-dispatch `Next` closures.

## What Mediator does (the trick to borrow)

`martinothamar/Mediator` composes the nested delegate chain **once** with a reverse `for`-loop and, for
singleton-lifetime handlers (`CachingMode.Eager`, the default), caches the head into `_rootHandler`:

```csharp
for (int i = behaviors.Length - 1; i >= 0; i--)
{
    var next = handler;            // capture current tail
    var b = behaviors[i];
    handler = (msg, ct) => b.Handle(msg, next, ct);
}
_rootHandler = handler;           // composed ONCE; dispatch just invokes it
```

Plus `Unsafe.As<T[]>` to reinterpret the `GetServices()` result as `T[]` without copying when the DI
container's backing store is already an array.

Net effect: zero per-dispatch composition and zero per-dispatch closure allocation for the cached path;
dispatch is a single delegate invocation through the chain.

## Proposed approach (layered — land incrementally)

1. **Compose in the constructor, not per dispatch.** Build the nested `RequestHandlerDelegate` chain
   once in the proxy ctor (reverse loop like Mediator), store the head as a field; `HandleAsync` invokes
   it directly. Removes the recursion frames; for scoped-with-multiple-dispatches it removes repeat
   closure allocation. Apply identically to `ProxyStreamRequestHandler`.

2. **Cache the composed chain per request type at a singleton level.** Mirror
   `EventDispatcher._behaviorCache` but cache the *composed delegate structure* rather than just the
   sorted array, re-binding scoped behavior instances per scope. Removes composition cost across scopes.

3. **Optional eager/singleton caching mode** for stateless behaviors (Mediator's `CachingMode.Eager`):
   when handler + behaviors are all singletons, compose once at startup → zero per-dispatch work.

4. **`Unsafe.As<T[]>` on the cached path** to skip the `GetServices()` → `ToArray()`/`ToImmutableArray()`
   copy. Only worth it once a cache exists (sorting still needs one copy on first build).

## Constraints / risks

- **Lifetime.** Behaviors are `Scoped`; a process-wide singleton chain cannot capture instances. Cache
  the *structure* (order + which behavior types), resolve concrete instances per scope. Eager singleton
  caching applies only when every participant is singleton.
- **Ordering must be preserved.** Keep `IOrderedPipelineBehavior` ordering and the stable-sort
  registration-order tie-break (`PipelineBehaviorOrdering.OrderOf`).
- **Request mutability.** Synapse's `Next(inRequest, ct)` lets a behavior swap the request; the composed
  chain must keep passing the request through the delegate parameter (do not close over the original).
- **Streaming + cancellation.** `ProxyStreamRequestHandler` threads `[EnumeratorCancellation]`; preserve
  cancellation semantics when flattening the recursion.

## Validation

- BenchmarkDotNet in `benchmarks/SynapseBenchmark`: cold + warm, **allocs/op** and ns/op with 0 / 1 / 3
  behaviors, before vs after — this is the metric that justifies the change (CLAUDE.md: benchmarks for
  hot-path changes).
- `dotnet test test/Synapse.Tests` — behavior unchanged (ordering, short-circuit, error propagation).
- Apply across all three drivers: `ProxyRequestHandler`, `ProxyStreamRequestHandler`, `EventDispatcher`.

## Files in scope

- `src/Synapse/ProxyRequestHandler.cs`
- `src/Synapse/ProxyStreamRequestHandler.cs`
- `src/Synapse/Publish/EventDispatcher.cs`
- `src/Synapse/Pipelines/PipelineBehaviorOrdering.cs` (ordering reused, not changed)
- `benchmarks/SynapseBenchmark` (new/updated benchmarks)
