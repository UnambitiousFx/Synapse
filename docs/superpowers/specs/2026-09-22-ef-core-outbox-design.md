# EF Core transactional outbox storage — Design

Issue: #92.

## Goal

Ship a real, transactional `IEventOutboxStorage` implementation backed by EF Core, so adopters stop
re-implementing the same storage (and its transactional pitfalls) themselves. Multi-`DbContext`
friendly: several contexts sharing one connection/transaction (modular monolith) must be able to
enlist the outbox write in the same transaction as the business write.

## Scope

This issue ships `UnambitiousFx.Synapse.Outbox.EntityFrameworkCore` only. Deferred to follow-up
issues (filed after this merges, not built here):

- A plain Npgsql/SQL Server storage variant with no EF Core dependency.
- A multi-instance dispatcher hosted service with row-claiming (`FOR UPDATE SKIP LOCKED` or
  equivalent).

Retry/back-off and dead-letter state are **not** new design here: `IEventOutboxStorage` already
carries `MarkAsFailedAsync(id, reason, deadLetter, nextAttemptAt)` from earlier work; this package
only has to persist that state faithfully.

## Prerequisite fix: `SetEventOutboxStorage` lifetime (`src/Synapse`)

`SynapseConfig.cs` registers whatever `SetEventOutboxStorage<T>()` is given as **Singleton**
(`services.AddSingleton(typeof(IEventOutboxStorage), _eventOutBoxStorage);`, `SynapseConfig.cs:451`).
That is correct for the stateful default, `InMemoryEventOutboxStorage`, but wrong for any storage
with a scoped dependency — including a `DbContext`. It silently breaks the DIY `EfCoreEventOutboxStorage`
sample already shown in `docs/docs/outbox.mdx` today (constructing a scoped `DbContext` from a
singleton throws when scope validation is on, and is unsafe even when it doesn't throw).

Fix, scoped to this PR, in `src/Synapse/ISynapseConfig.cs` and `src/Synapse/SynapseConfig.cs`:

```csharp
ISynapseConfig SetEventOutboxStorage<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
TEventOutboxStorage>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
    where TEventOutboxStorage : class, IEventOutboxStorage;
```

```csharp
private ServiceLifetime _eventOutBoxStorageLifetime = ServiceLifetime.Scoped;

public ISynapseConfig SetEventOutboxStorage<...>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
    where TEventOutboxStorage : class, IEventOutboxStorage
{
    _eventOutBoxStorage = typeof(TEventOutboxStorage);
    _eventOutBoxStorageLifetime = lifetime;
    return this;
}
```

Registration becomes `services.Add(new ServiceDescriptor(typeof(IEventOutboxStorage), _eventOutBoxStorage, _eventOutBoxStorageLifetime));`.
The untouched default path (nobody calls `SetEventOutboxStorage`) is unaffected: `_eventOutBoxStorage`
still defaults to `typeof(InMemoryEventOutboxStorage)`, registered Singleton via a dedicated,
unconditional `services.AddSingleton<IEventOutboxStorage, InMemoryEventOutboxStorage>()` call kept
separate from the configurable path — i.e. the field's own default lifetime is irrelevant because the
"nothing configured" branch never reads `_eventOutBoxStorageLifetime`; it registers the in-memory
default explicitly, Singleton, unconditionally. Only a call to `SetEventOutboxStorage<T>()` uses the
`_eventOutBoxStorageLifetime` field, and that call's own default parameter value (`Scoped`) is what
callers get if they don't pass a lifetime.

This is a behavior change for any existing adopter who already calls `SetEventOutboxStorage<T>()`
with a **stateless, thread-safe** custom storage: it moves from Singleton to Scoped by default,
meaning more instantiations (correctness is unaffected; a stateless type works under either lifetime).
An adopter who needs Singleton back can pass `ServiceLifetime.Singleton` explicitly. Documented as a
known issue (bug found and fixed on this branch): Area `Core DI`.

## Package: `UnambitiousFx.Synapse.Outbox.EntityFrameworkCore`

`src/Synapse.Outbox.EntityFrameworkCore/`, multi-targets `net8.0;net9.0;net10.0` like every other
`src/` project (`$(LibraryTargetFrameworks)` from `build.props`). References only
`Synapse.Abstractions` (project reference) plus `Microsoft.EntityFrameworkCore` and
`Microsoft.EntityFrameworkCore.Relational` — **not** the `Synapse` runtime project; the storage only
needs the abstractions, not the pipeline/DI wiring. `IsAotCompatible` is `false` for this project
only, with a `<!-- comment -->` note why: EF Core's dynamic model building and reflection-based JSON
(de)serialization of arbitrary `IEvent` payload types are not AOT-safe without additional ceremony
(compiled models, per-type `JsonSerializerContext`) that is out of scope here.

Package versions (`Directory.Packages.props`, conditional per `$(TargetFramework)` exactly like the
existing `Microsoft.Extensions.*` rows):

| TFM | `Microsoft.EntityFrameworkCore` / `.Relational` | Test-only: `Microsoft.EntityFrameworkCore.Sqlite` |
|---|---|---|
| net8.0 | 8.0.31 | 8.0.31 |
| net9.0 | 9.0.20 | 9.0.20 |
| net10.0 | 10.0.12 | 10.0.12 |

### `OutboxEntity` (internal to this package)

Field-for-field mirror of `InMemoryEventOutboxStorage.Item`, but with the event payload serialized
instead of held as a live reference:

```csharp
internal sealed class OutboxEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;   // Type.AssemblyQualifiedName
    public string Payload { get; set; } = string.Empty;      // JSON
    public string Headers { get; set; } = string.Empty;      // JSON (Dictionary<string,string>)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public bool Processed { get; set; }
    public bool DeadLetter { get; set; }
    public bool Discarded { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}
```

### `OutboxEntityTypeConfiguration : IEntityTypeConfiguration<OutboxEntity>`

Public, so a project that wants the table inside its own `DbContext` (own migrations, own schema)
can apply it directly. Constructor takes an optional `string schema = "outbox"`. Maps to table
`outbox_events` in that schema. `Id` is the key. `EventType`/`Payload`/`Headers` map to unbounded
text columns (no provider-specific `jsonb`/`nvarchar(max)` annotation — stays provider-neutral; a
project that wants a provider-specific column type applies its own `HasColumnType` after this
configuration runs, since EF Core configuration is additive/overridable in `OnModelCreating`). One
index on `(Processed, DeadLetter, Discarded, NextAttemptAt)` to keep `GetPendingEventsAsync` and the
count queries efficient; one index on `DeadLetter` for `GetDeadLetterEventsAsync`.

### `OutboxDbContext : DbContext`

```csharp
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxEntity> OutboxEvents => Set<OutboxEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());
    }
}
```

No `Migrations/` folder ships in this package: migrations are provider-specific (SQL dialect,
column types), and a package targeting "any EF Core provider" cannot pre-bake one dialect's
migration and expect it to apply cleanly on another. This mirrors how `IdentityDbContext` ships no
migrations either. The docs page tells the adopter to run
`dotnet ef migrations add InitialOutbox --context OutboxDbContext` in their own project once they've
registered `OutboxDbContext` with a provider.

### `EfCoreEventOutboxStorage<TContext>` where `TContext : DbContext`

```csharp
public sealed class EfCoreEventOutboxStorage<TContext>(TContext context)
    : IEventOutboxStorage, IDiscardableOutboxStorage
    where TContext : DbContext
```

- Constructor takes `TContext` directly (constructor injection) — this is what makes ambient
  transaction sharing work: the app controls how/when `TContext` was created and what transaction
  it is already enlisted in (its own `SaveChangesAsync`'s implicit transaction, or an explicit one
  shared across contexts via `Database.UseTransactionAsync`). The storage never calls
  `BeginTransaction` itself.
- `AddAsync<TEvent>`: `JsonSerializer.Serialize(@event, typeof(TEvent))` for `Payload`,
  `typeof(TEvent).AssemblyQualifiedName!` for `EventType`, headers serialized the same way; adds the
  row to `context.Set<OutboxEntity>()`, immediately `await context.SaveChangesAsync(ct)`. Records the
  new `(event, id)` pair (by reference) in a private `Dictionary<IEvent, Guid>` built with
  `ReferenceEqualityComparer.Instance`, used only by `DiscardAsync`. Exceptions from `SaveChangesAsync`
  (e.g. `DbUpdateException`) are caught and returned as `Result.Failure(ex.Message)`, matching the
  interface's existing `Result`-based failure contract elsewhere.
- `GetPendingEventsAsync`: `Set<OutboxEntity>().Where(e => !e.Processed && !e.DeadLetter &&
  !e.Discarded && (e.NextAttemptAt == null || e.NextAttemptAt <= now)).OrderBy(e => e.CreatedAt)
  .ToListAsync(ct)`, then deserializes each row to an `OutboxEntry` via `Type.GetType(e.EventType,
  throwOnError: true)!` and `(IEvent)JsonSerializer.Deserialize(e.Payload, type)!`. A row whose type
  can no longer be resolved (renamed/removed event type) throws — this surfaces as a real
  operational problem rather than being silently dropped, consistent with there being no `Result`
  return channel on this method to report a partial failure through.
- `MarkAsProcessedAsync(id, ct)`: finds the row by id (404 → `Result.Failure`, matching
  `InMemoryEventOutboxStorage`'s message wording), sets `Processed = true`, `ProcessedAt = now`,
  `LastError = null`, `NextAttemptAt = null`, saves.
- `MarkAsFailedAsync(id, reason, deadLetter, nextAttemptAt, ct)`: finds the row, increments
  `Attempts`, sets `LastError = reason`; if `deadLetter`, sets `DeadLetter = true` and clears
  `NextAttemptAt`; otherwise sets `NextAttemptAt`. Saves.
- `ClearAsync`: `ExecuteDeleteAsync()` on the full set (no filter — matches
  `InMemoryEventOutboxStorage.ClearAsync`'s unconditional clear).
- `GetDeadLetterEventsAsync`, `GetAttemptCountAsync`, `GetPendingCountAsync`,
  `GetRetryingCountAsync`, `GetDeadLetterCountAsync`, `GetOldestPendingAgeAsync`: direct LINQ
  translations of `InMemoryEventOutboxStorage`'s equivalent predicates against `Set<OutboxEntity>()`,
  using `CountAsync`/`AnyAsync`/`MinAsync` rather than in-memory `Count`/`Any`/`OrderBy().First()`.
- `IDiscardableOutboxStorage.DiscardAsync(events, ct)`: for each event in `events`, look up its id in
  the private reference-keyed dictionary (skip events this instance never stored — matches
  `InMemoryEventOutboxStorage`'s "matched by reference" contract, which only ever matches what this
  process itself stored); for the ids found, load the corresponding un-processed, non-dead-lettered
  rows and set `Discarded = true`; save. Because `EfCoreEventOutboxStorage<TContext>` is registered
  Scoped (see the prerequisite fix), this dictionary's lifetime is naturally one request/scope —
  it cannot leak across requests the way a Singleton's would.

### `ServiceCollectionExtensions`

```csharp
public static IServiceCollection AddEfCoreEventOutbox<TContext>(this IServiceCollection services)
    where TContext : DbContext
    => services.AddScoped<EfCoreEventOutboxStorage<TContext>>();
```

Registers the concrete storage type only. The app still calls
`cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<TContext>>()` itself inside `AddSynapse(...)` —
this package does not reference `Synapse` to call that directly.

## Data flow / usage

Single `DbContext` (common case — outbox lives on the same context as business entities, or a
separate `OutboxDbContext` registered independently with no transaction sharing needed because
nothing else needs atomicity with it):

```csharp
services.AddDbContext<OutboxDbContext>(o => o.UseNpgsql(connectionString));
services.AddEfCoreEventOutbox<OutboxDbContext>();
services.AddSynapse(cfg =>
{
    cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<OutboxDbContext>>();
});
```

Multi-`DbContext` (modular monolith, one business context + the outbox context must commit or roll
back together):

```csharp
await using var tx = await businessContext.Database.BeginTransactionAsync(ct);
await outboxContext.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);

// business save
businessContext.Tasks.Add(task);
await businessContext.SaveChangesAsync(ct);

// outbox write — same underlying DbTransaction as the business save above
await emitter.EmitAsync(new TaskCreatedEvent(task.Id), EmitMode.Outbox, ct);

await tx.CommitAsync(ct);   // both commit together; either rolls back together
```

## Error handling

- `Result`-returning members wrap `DbUpdateException`/`DbUpdateConcurrencyException` into
  `Result.Failure`; they never throw for an ordinary persistence failure.
- `GetPendingEventsAsync` throws on an unresolvable stored `EventType` (see above) since it has no
  `Result` channel to report a partial failure through — documented in the XML doc and the outbox
  docs page as an operational hazard of the `AssemblyQualifiedName` approach (renaming/removing an
  event type after entries referencing it are already pending).
- No new validation of `OutboxOptions`; retry/back-off timing is computed by the existing
  `IOutboxManager` (in `Synapse`), this storage only persists what it's told.

## Testing

New `test/Synapse.Outbox.EntityFrameworkCore.Tests` (multi-targeted the same as other test
projects), using the `Microsoft.EntityFrameworkCore.Sqlite` provider against a shared-cache
in-memory database (`DataSource=file:<unique-name>?mode=memory&cache=shared`, one connection kept
open for the database's lifetime) — a real relational engine with real transactions and isolation,
no external dependency, no Docker/Testcontainers needed:

- `AddAsync` inside a transaction, then roll back → `GetPendingEventsAsync` returns empty (rollback
  removes the pending entry).
- A second `DbContext` on a second connection to the same shared-cache database does not see a row
  added inside an uncommitted transaction on the first connection (proves isolation).
- Multi-`DbContext`: two `DbContext`s share one `DbTransaction` via `Database.UseTransactionAsync`;
  a business-table insert on one and an outbox `AddAsync` on the other commit together, and roll
  back together.
- `MarkAsProcessedAsync` / `MarkAsFailedAsync` (retry path and dead-letter path) transition state as
  expected; `GetAttemptCountAsync`, `GetRetryingCountAsync`, `GetDeadLetterCountAsync`,
  `GetPendingCountAsync`, `GetOldestPendingAgeAsync` reflect it.
- `DiscardAsync` marks only the rows matching the given event references and only while still
  pending; already-processed or already-dead-lettered rows are untouched.
- `OutboxEntityTypeConfiguration` applied inside a caller-owned `DbContext` (alongside an unrelated
  entity) builds and queries correctly — proves the "embed in your own context" path.
- `ClearAsync` removes everything unconditionally.

`test/Synapse.Tests` (existing project): `SetEventOutboxStorage` lifetime —
`AddSynapse()` with nothing configured still registers `InMemoryEventOutboxStorage` Singleton;
`SetEventOutboxStorage<T>()` with no argument registers `T` Scoped; passing
`ServiceLifetime.Singleton` explicitly is honored; a scoped custom storage resolves without throwing
when the host's `ServiceProviderOptions.ValidateScopes = true`.

## Docs

- `docs/docs/outbox.mdx`, "Replace the storage for production" section: replace the current DIY
  `EfCoreEventOutboxStorage` sample (which is Singleton-broken under today's `SetEventOutboxStorage`)
  with a short pointer to the new package and a link to the dedicated page below.
- New `docs/docs/outbox-entityframeworkcore.mdx`: install, `OutboxDbContext` vs embedding via
  `OutboxEntityTypeConfiguration`, the migrations note (consumer generates their own), the
  multi-`DbContext` transaction-sharing snippet from "Data flow" above, and a limitations note: no
  built-in multi-instance claim/locking yet (`GetPendingEventsAsync` has no row-locking), forward-
  linking to the follow-up dispatcher-hosted-service issue.

## Changelog

`docs/known-issues/`: the `SetEventOutboxStorage` Singleton-only bug — found and fixed on this
branch. Area: `Core DI`. Three files per the `changelog` skill (detail file, README row, `changelog.mdx`
row).

## Out of scope

- Raw Npgsql/SQL Server storage without EF Core (follow-up issue).
- Multi-instance dispatcher hosted service with row-claiming / `SKIP LOCKED` (follow-up issue);
  `GetPendingEventsAsync` here has no locking, so running two instances of the existing
  `IOutboxManager.ProcessPendingAsync` loop concurrently against the same EF Core storage can still
  double-dispatch — same limitation the in-memory storage already has today, not a regression.
- Shipped EF Core migrations.
- Provider-specific column types (`jsonb`, etc.) — left to the adopter to layer on if wanted.
- Changing `InMemoryEventOutboxStorage`'s own registration or behavior.

## Follow-up issues to file after merge

1. Plain Npgsql/SQL Server transactional outbox storage (no EF Core dependency).
2. Multi-instance dispatcher hosted service with `FOR UPDATE SKIP LOCKED` (or provider equivalent)
   row-claiming, replacing/augmenting manual `IOutboxCommit.CommitAsync()` calls.
