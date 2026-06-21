---
sidebar_position: 5
---

# Streaming

Streaming lets a handler yield results one item at a time via `IAsyncEnumerable<Result<TItem>>`. Use it when the full result set is too large to materialize in memory, or when you want to start sending data to the client before all items are ready.

## Define a streaming request

A streaming request implements `IStreamRequest<TItem>`:

```csharp
using UnambitiousFx.Synapse.Abstractions;

public record StreamTasksQuery(string? Filter) : IStreamRequest<TaskDto>;
```

## Implement the handler

```csharp
using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

public class StreamTasksQueryHandler : IStreamRequestHandler<StreamTasksQuery, TaskDto>
{
    private readonly ITaskRepository _repository;

    public StreamTasksQueryHandler(ITaskRepository repository) => _repository = repository;

    public async IAsyncEnumerable<Result<TaskDto>> HandleAsync(
        StreamTasksQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var task in _repository.StreamAsync(query.Filter, ct))
        {
            yield return Result.Success(TaskDto.From(task));
        }
    }
}
```

Key points:
- Return type is `IAsyncEnumerable<Result<TItem>>` — each item is independently wrapped in a `Result`.
- Decorate `CancellationToken` with `[EnumeratorCancellation]` so that cancellation tokens passed to `await foreach` are forwarded correctly.
- Yield `Result.Success(item)` for successful items; yield `Result.Failure<TItem>(...)` to signal a per-item error without aborting the entire stream.

## Register the handler

```csharp
services.AddSynapse(cfg =>
{
    cfg.RegisterStreamRequestHandler<StreamTasksQueryHandler, StreamTasksQuery, TaskDto>();
});
```

## Invoke the stream

Use `IInvoker.InvokeStreamAsync<TItem>`. The item type is inferred from `IStreamRequest<TItem>`:

```csharp
await foreach (var result in invoker.InvokeStreamAsync(new StreamTasksQuery(filter: null), ct))
{
    if (result.TryGet(out var dto, out var error))
        Console.WriteLine(dto.Title);
    else
        Console.WriteLine($"Item error: {error}");
}
```

## Per-item error handling

Because each item is a `Result<TItem>`, a failure in one item does not cancel the rest of the stream. This is useful when some items may be unavailable or fail to deserialize:

```csharp
await foreach (var result in invoker.InvokeStreamAsync(new StreamTasksQuery(), ct))
{
    result.Match(
        success: dto   => ProcessItem(dto),
        failure: error => LogError(error));      // stream continues after this
}
```

## ASP.NET Core — streaming HTTP responses

When using `IHttpInvoker` (from `UnambitiousFx.Synapse.AspNetCore`), call `InvokeStreamAsync` to get back an `IAsyncEnumerable<TItem>` with successful items already unwrapped and failures silently skipped:

```csharp
app.MapGet("/tasks/stream", ([FromServices] IHttpInvoker invoker, CancellationToken ct) =>
    invoker.InvokeStreamAsync(new StreamTasksQuery(), ct));
```

Returning `IAsyncEnumerable<T>` directly from a Minimal API endpoint causes ASP.NET Core to stream the JSON array to the client rather than buffering the entire response.

## See also

- [Commands and Queries](./commands-and-queries) — for non-streaming requests.
- [ASP.NET Core Integration](./aspnetcore) — `IHttpInvoker.InvokeStreamAsync` and HTTP streaming.
- [Pipeline Behaviors](./pipelines) — `IStreamRequestPipelineBehavior` for stream cross-cutting concerns.
