# [Bug]: In-memory outbox leaks events of failed commands

**Severity:** Medium
**Area:** Outbox
**Discovered on:** `main` (2.0.1), .NET 10, found while integrating Synapse into a modular monolith (issue #93)
**Status:** ✅ **Resolved** on `fix/outbox-discard-on-failure` — see [Resolution](#resolution).

> **TL;DR.** The default `InMemoryEventOutboxStorage` is not enlisted in any transaction, so an event stored with
> `EmitMode.Outbox` by a command that then failed stayed in the store and was dispatched by the next
> `IOutboxCommit.CommitAsync`, usually from an unrelated request. An opt-in `OutboxDiscardOnFailureBehavior` now takes a
> failed request's events back, and a Production host logs a startup warning.

---

## Describe the bug

The in-memory storage is a Singleton holding one flat collection, and `CommitAsync` dispatches every pending entry
regardless of which scope stored it. Nothing ties an entry to the outcome of the request that stored it.

If a command emits with `EmitMode.Outbox` and then returns a failed `Result` (or throws) after its own database
transaction rolled back, the entry stays pending. The next `CommitAsync`, typically triggered by an unrelated
successful request, dispatches it. Subscribers react to something that never happened.

---

## Steps to reproduce

1. Use the default in-memory outbox storage.
2. Send a command whose handler calls `EmitAsync(new SomethingHappened(), EmitMode.Outbox)` and then returns
   `Result.Failure(...)`.
3. Send any other command whose handler calls `IOutboxCommit.CommitAsync()`.

---

## Expected behavior

`SomethingHappened` is never dispatched: the command that emitted it failed.

## Actual behavior

`SomethingHappened` is dispatched to its handlers by the second command's commit.

---

## Code sample

```csharp
public async ValueTask<Result> HandleAsync(FailingCommand request, CancellationToken ct = default)
{
    await _emitter.EmitAsync(new SomethingHappened(), EmitMode.Outbox, ct);
    return Result.Failure("the command failed after emitting");
}
```

---

## Library version

`main` (2.0.1)

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

`IEventOutboxStorage.AddAsync` has no notion of the request's outcome, and the in-memory implementation is
deliberately global (see the remarks on `InMemoryEventOutboxStorage`) so that processing can run from any scope,
including a background one. A transactional storage gets atomicity from the database transaction; this one has nothing
to stand in for the rollback.

### Resolution

- `IDiscardableOutboxStorage` (new, optional): a storage that can take back not-yet-dispatched entries by event
  instance. `InMemoryEventOutboxStorage` implements it, matching by reference so two value-equal emissions stay
  distinct. `IEventOutboxStorage` is unchanged, so custom storages are unaffected.
- The scoped `OutboxManager` remembers the event instances it stored, and only when the storage is discardable.
  `IOutboxDiscard.DiscardStoredAsync` (new, public) discards them.
- `OutboxDiscardOnFailureBehavior<TRequest>` / `<TRequest, TResponse>` (new) call it when the request returns a
  failed `Result` or throws. It runs outermost (`IOrderedPipelineBehavior.First`). Wire it with
  `cfg.RegisterOutboxDiscardOnFailure<...>()` or `[assembly: SynapseGlobalBehavior(typeof(OutboxDiscardOnFailureBehavior<>))]`.
- `InMemoryOutboxProductionCheck` (new hosted service) logs a warning at startup when the in-memory storage is
  registered and `IHostEnvironment.IsProduction()`.
- `InMemoryEventOutboxStorage` XML docs, `outbox.mdx` and `pipelines.mdx` state that the storage is not transactional.

The behavior is **opt-in**: pipeline behaviors in Synapse are registered per request type, so there is no
default-on hook without touching the hot path of every request. Discarding is scope-wide (everything the DI scope
stored), and events already flushed by an explicit `CommitAsync` inside the failed handler cannot be taken back.

**Verification.** `OutboxDiscardOnFailureBehaviorTests` (failed `Result`, throw, success keeps the event, other scope
untouched, and a control test showing the leak without the behavior), `InMemoryEventOutboxStorageTests`,
`OutboxManagerTests` and `InMemoryOutboxProductionCheckTests`; the full solution test suite passes.
