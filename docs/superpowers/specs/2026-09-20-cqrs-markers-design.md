# ICommand / IQuery marker interfaces — Design

Issue: #94.

## Goal

Let a project tell commands from queries at the type level, so behaviors (transactions, idempotency, caching, scope
checks) can be scoped to one side with a generic constraint instead of each project defining its own markers.

## Public API (`Synapse.Abstractions`, namespace `UnambitiousFx.Synapse.Abstractions`)

Additive; no existing type changes. One file per type, XML docs on each.

```csharp
public interface ICommand : IRequest;
public interface ICommand<out TResponse> : IRequest<TResponse>;
public interface IQuery<out TResponse> : IRequest<TResponse>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : notnull;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : notnull;
```

- Variance matches `IRequest<out TResponse>` and `IRequestHandler<in TRequest, TResponse>`.
- No non-generic `IQuery` (a query always returns data).
- The handler aliases are pure renames of the interface a handler implements: they inherit `IRequestHandler`, so DI
  registration, the source generator, `IPipelineDescriber`, `ValidateSynapse()` and the SYN analyzers (which read
  `AllInterfaces`) treat them like any handler. No new attribute: handlers are still declared with
  `[RequestHandler<...>]`.

## Scoping behaviors (no new API)

Behaviors are scoped with a generic constraint, using mechanisms that already exist:

```csharp
[PipelineBehavior]
public sealed class TransactionBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
    where TResponse : notnull { ... }
```

- Source generator (`[PipelineBehavior]`, `[assembly: SynapseGlobalBehavior]`): already honors concrete constraints
  when cross-producting behaviors with handlers.
- Runtime (`AddOpenGeneric...PipelineBehavior`): DI skips an open generic whose constraints are not met. This path
  keeps its documented Native-AOT limit (value-type responses); docs point to `[PipelineBehavior]` for AOT.
- The proposal's `AddCommandPipelineBehavior` / `AddQueryPipelineBehavior` helpers are **not** added: they would
  duplicate constraints.

## Out of scope

- Migrating `examples/MinimalApi` to the markers.
- Making `CqrsBoundaryEnforcementBehavior` distinguish commands from queries (its docs describe rules that the
  implementation does not enforce; a separate design question).
- Stream markers, `IQuery` without a response.
- A new package (the interfaces live in `Synapse.Abstractions`).

## Testing

- **Core** (`test/Synapse.Tests`): shape tests (assignability, variance via reference conversion); commands and queries
  dispatched through `IInvoker` with handlers implemented as `ICommandHandler` / `IQueryHandler` and registered with
  `RegisterRequestHandler<...>`; a behavior constrained to `ICommand<TResponse>` registered through the runtime
  `AddOpenGenericRequestWithResponsePipelineBehavior` runs for a command and not for a query; `ValidateSynapse()` is
  clean and `IPipelineDescriber` describes the pipeline for marker-based handlers.
- **Generator** (`test/Synapse.Generator.Tests`): a `[PipelineBehavior]` constrained to `ICommand<TResponse>` is
  cross-produced only with command handlers and not with a query handler; `[RequestHandler]` on a class that
  implements `ICommandHandler` / `IQueryHandler` is emitted normally.
- **Analyzers** (`test/Synapse.Generator.Tests/Analyzers`): SYN101 does not report a command/query whose handler
  implements `ICommandHandler` / `IQueryHandler`; SYN104 treats such an unattributed class as a handler.

## Docs

- `docs/docs/commands-and-queries.mdx`: the marker table now lists `ICommand` / `ICommand<T>` / `IQuery<T>` as the
  optional intent markers, with the handler aliases and a short example.
- `docs/docs/pipelines.mdx`: new section "Applying a behavior to commands or queries only" with the constraint
  snippet for `[PipelineBehavior]`, the runtime note and its AOT limit.
- No known-issue entry (feature, not a bug fix).
