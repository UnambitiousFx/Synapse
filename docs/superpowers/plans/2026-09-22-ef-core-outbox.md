# EF Core transactional outbox storage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `UnambitiousFx.Synapse.Outbox.EntityFrameworkCore`, a transactional, multi-`DbContext`-friendly `IEventOutboxStorage` implementation, and fix the `SetEventOutboxStorage` lifetime bug that blocks any scoped-dependency custom storage (including this one) from working safely.

**Architecture:** A small, self-contained fix to `src/Synapse`'s DI registration (Task 1), then a new leaf package under `src/` that depends only on `Synapse.Abstractions` — no dependency on the `Synapse` runtime project — built up in three stages: schema/context (Task 2), storage logic (Task 3), transactional proof tests (Task 4), then docs/changelog (Task 5).

**Tech Stack:** .NET 8/9/10, EF Core (`Microsoft.EntityFrameworkCore` + `.Relational`), `Microsoft.EntityFrameworkCore.Sqlite` (test-only, shared-cache in-memory), xUnit v3 (Microsoft Testing Platform), `UnambitiousFx.Functional` (`Result`).

**Spec:** `docs/superpowers/specs/2026-09-22-ef-core-outbox-design.md`

## Global Constraints

- Every `src/` project multi-targets `net8.0;net9.0;net10.0` via `$(LibraryTargetFrameworks)` (from `build.props`) except this new package's `IsAotCompatible`, which is `false` (documented reason: EF Core dynamic model + reflection JSON are not AOT-safe without extra ceremony out of scope here). Every other `src/` project keeps `IsAotCompatible=true` — do not touch that for existing projects.
- `TreatWarningsAsErrors=true` and `WarningsAsErrors=true` repo-wide (`build.props`); new code must build warning-free.
- Central package management (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`): add `<PackageVersion>` there, never a `Version=` attribute on a `<PackageReference>` in a project file.
- EF Core package versions pinned per `$(TargetFramework)`, matching the existing `Microsoft.Extensions.*` per-TFM blocks: net8.0 → `8.0.31`, net9.0 → `9.0.20`, net10.0 → `10.0.12`.
- New/changed public APIs get XML doc comments (`<summary>`/`<param>`/`<returns>`).
- Tests: AAA (Arrange/Act/Assert) with comments, `Method_Scenario_ExpectedBehavior` naming, `TestContext.Current.CancellationToken` for cancellation tokens (xUnit v3 pattern used throughout the repo), `[TestSubject(typeof(X))]` from `JetBrains.Annotations` on the primary test class for a type.
- Run tests with `dotnet test --project <csproj> -f <tfm> --filter-class "*ClassName"` — **not** `--filter`, which silently runs zero tests under Microsoft Testing Platform.
- No multi-line `.Replace("...")` on C# raw string literals in test code — Windows CI checks out CRLF and a `\n`-based multi-line match breaks silently there. Compose multi-line expected strings from concatenated single-line constants instead.
- A bug found **and** fixed on this branch (Task 1) is documented via the `changelog` skill: `docs/known-issues/070-*.md` + `docs/known-issues/README.md` row + `docs/docs/changelog.mdx` row, Area `Core DI`, all three consistent (Task 5).
- Deviation from spec's "`OutboxEntity` internal" note: `OutboxEntity` must be **public**, not internal. A public `OutboxEntityTypeConfiguration : IEntityTypeConfiguration<OutboxEntity>` and a public `OutboxDbContext.OutboxEvents` property of type `DbSet<OutboxEntity>` both require `OutboxEntity` itself to be public — an internal type used in a public member's signature is a compiler error (CS0053). This does not change the spec's intent (the entity was always meant to be usable from a caller's own `DbContext`); it corrects an internal inconsistency in the spec text. All tasks below use `public sealed class OutboxEntity`.

---

### Task 1: Fix `SetEventOutboxStorage` Singleton-only registration

**Files:**
- Modify: `src/Synapse/ISynapseConfig.cs:254-257`
- Modify: `src/Synapse/SynapseConfig.cs:41` (field), `src/Synapse/SynapseConfig.cs:375-381` (method), `src/Synapse/SynapseConfig.cs:451` (registration)
- Test: `test/Synapse.Tests/SynapseConfigEventOutboxStorageLifetimeTests.cs` (new)

**Interfaces:**
- Consumes: nothing new — modifies existing `ISynapseConfig.SetEventOutboxStorage<T>()`.
- Produces: `ISynapseConfig SetEventOutboxStorage<TEventOutboxStorage>(ServiceLifetime lifetime = ServiceLifetime.Scoped)` — Task 3's DI wiring docs and Task 5's docs page both call this with no argument and expect Scoped registration.

- [ ] **Step 1: Write the failing tests**

Create `test/Synapse.Tests/SynapseConfigEventOutboxStorageLifetimeTests.cs`:

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse.Tests;

[TestSubject(typeof(SynapseConfig))]
public sealed class SynapseConfigEventOutboxStorageLifetimeTests
{
    [Fact]
    public void AddSynapse_WithNoStorageConfigured_RegistersInMemoryStorageAsSingleton()
    {
        // Arrange (Given) — the default must stay exactly as it is today: no one calls
        // SetEventOutboxStorage, so InMemoryEventOutboxStorage keeps its Singleton lifetime.
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapse(_ => { });

        // Assert (Then)
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventOutboxStorage));
        Assert.Equal(typeof(InMemoryEventOutboxStorage), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void SetEventOutboxStorage_WithNoLifetimeArgument_RegistersScoped()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapse(cfg => cfg.SetEventOutboxStorage<RecordingOutboxStorage>());

        // Assert (Then)
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventOutboxStorage));
        Assert.Equal(typeof(RecordingOutboxStorage), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void SetEventOutboxStorage_WithExplicitSingleton_HonorsIt()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapse(cfg =>
            cfg.SetEventOutboxStorage<RecordingOutboxStorage>(ServiceLifetime.Singleton));

        // Assert (Then)
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventOutboxStorage));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void SetEventOutboxStorage_ScopedCustomStorage_ResolvesUnderValidatedScopes()
    {
        // Arrange (Given) — a scoped custom storage resolving cleanly with ValidateScopes = true is
        // the actual bug this fix closes: today it throws InvalidOperationException here.
        var services = new ServiceCollection();
        services.AddSynapse(cfg => cfg.SetEventOutboxStorage<RecordingOutboxStorage>());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Act (When)
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>();

        // Assert (Then)
        Assert.IsType<RecordingOutboxStorage>(storage);
    }

    private sealed class RecordingOutboxStorage : IEventOutboxStorage
    {
        public ValueTask<UnambitiousFx.Functional.Result> AddAsync<TEvent>(TEvent @event,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
            CancellationToken cancellationToken = default)
            => new(Array.Empty<OutboxEntry>() as IReadOnlyList<OutboxEntry>);

        public ValueTask<UnambitiousFx.Functional.Result> MarkAsProcessedAsync(Guid id,
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<UnambitiousFx.Functional.Result> ClearAsync(
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<UnambitiousFx.Functional.Result> MarkAsFailedAsync(Guid id,
            string reason,
            bool deadLetter,
            DateTimeOffset? nextAttemptAt = null,
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
            CancellationToken cancellationToken = default)
            => new(Array.Empty<OutboxEntry>() as IReadOnlyList<OutboxEntry>);

        public ValueTask<int?> GetAttemptCountAsync(Guid id,
            CancellationToken cancellationToken = default)
            => new((int?)null);

        public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<TimeSpan?> GetOldestPendingAgeAsync(
            CancellationToken cancellationToken = default)
            => new((TimeSpan?)null);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project test/Synapse.Tests/Synapse.Tests.csproj -f net10.0 --filter-class "*SynapseConfigEventOutboxStorageLifetimeTests"`

Expected: compile error or `SetEventOutboxStorage<RecordingOutboxStorage>(ServiceLifetime.Singleton)` overload not found (the single-generic-parameter, no-argument method exists today but takes no `ServiceLifetime` parameter) — confirms the test exercises the not-yet-built API. If it compiles because the no-arg call resolves but the lifetime assertions fail (`Singleton` where `Scoped` expected), that is also an acceptable RED — either way, the two lifetime-asserting tests must fail before the fix.

- [ ] **Step 3: Add the `ServiceLifetime` parameter to `ISynapseConfig.SetEventOutboxStorage`**

In `src/Synapse/ISynapseConfig.cs`, add the using and change the declaration:

```csharp
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Publish.Outbox;
```

Replace:

```csharp
    /// <summary>
    ///     Configures the mediator to use the specified implementation for event outbox storage.
    /// </summary>
    ISynapseConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOutboxStorage>()
        where TEventOutboxStorage : class, IEventOutboxStorage;
```

with:

```csharp
    /// <summary>
    ///     Configures the mediator to use the specified implementation for event outbox storage.
    /// </summary>
    /// <param name="lifetime">
    ///     The DI lifetime to register the storage with. Defaults to <see cref="ServiceLifetime.Scoped"/>,
    ///     which is required for a storage backed by a scoped dependency such as a <c>DbContext</c>. Pass
    ///     <see cref="ServiceLifetime.Singleton"/> explicitly for a storage that is thread-safe and holds no
    ///     scoped dependency, such as the built-in in-memory one.
    /// </param>
    ISynapseConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOutboxStorage>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TEventOutboxStorage : class, IEventOutboxStorage;
```

- [ ] **Step 4: Update `SynapseConfig`**

In `src/Synapse/SynapseConfig.cs`, add a lifetime field next to the existing type field (around line 41):

```csharp
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _eventOutBoxStorage = typeof(InMemoryEventOutboxStorage);

    private ServiceLifetime _eventOutBoxStorageLifetime = ServiceLifetime.Singleton;
```

Replace the method (around line 375):

```csharp
    public ISynapseConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TEventOutboxStorage>()
        where TEventOutboxStorage : class, IEventOutboxStorage
    {
        _eventOutBoxStorage = typeof(TEventOutboxStorage);
        return this;
    }
```

with:

```csharp
    public ISynapseConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TEventOutboxStorage>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TEventOutboxStorage : class, IEventOutboxStorage
    {
        _eventOutBoxStorage = typeof(TEventOutboxStorage);
        _eventOutBoxStorageLifetime = lifetime;
        return this;
    }
```

Replace the registration line (around line 451):

```csharp
        services.AddSingleton(typeof(IEventOutboxStorage), _eventOutBoxStorage);
```

with:

```csharp
        services.Add(new ServiceDescriptor(typeof(IEventOutboxStorage), _eventOutBoxStorage,
            _eventOutBoxStorageLifetime));
```

This keeps the untouched default path identical: `_eventOutBoxStorage` still defaults to
`typeof(InMemoryEventOutboxStorage)` and `_eventOutBoxStorageLifetime` still defaults to
`ServiceLifetime.Singleton`, so nobody calling `SetEventOutboxStorage` results in the exact same
registration as today. Calling `SetEventOutboxStorage<T>()` with no argument now registers `T`
Scoped (the method parameter's own default); passing a lifetime explicitly overrides it.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --project test/Synapse.Tests/Synapse.Tests.csproj -f net10.0 --filter-class "*SynapseConfigEventOutboxStorageLifetimeTests"`

Expected: PASS, all 4 tests.

- [ ] **Step 6: Run the full `Synapse.Tests` project to check for regressions**

Run: `dotnet test --project test/Synapse.Tests/Synapse.Tests.csproj -f net10.0`

Expected: PASS. If any existing test constructs `SynapseConfig`/`AddSynapse` and asserts a Singleton
`IEventOutboxStorage` registration when a **custom** storage was configured via
`SetEventOutboxStorage<T>()` (search `grep -rn "SetEventOutboxStorage" test/Synapse.Tests` first), that
assertion needs updating to `ServiceLifetime.Scoped` to match the new default — this is the intended
behavior change, not a regression.

- [ ] **Step 7: Commit**

```bash
git add src/Synapse/ISynapseConfig.cs src/Synapse/SynapseConfig.cs test/Synapse.Tests/SynapseConfigEventOutboxStorageLifetimeTests.cs
git commit -m "fix(core): SetEventOutboxStorage now registers Scoped by default (#92)"
```

---

### Task 2: Scaffold the EF Core outbox package — entity, mapping, context

**Files:**
- Create: `src/Synapse.Outbox.EntityFrameworkCore/Synapse.Outbox.EntityFrameworkCore.csproj`
- Create: `src/Synapse.Outbox.EntityFrameworkCore/OutboxEntity.cs`
- Create: `src/Synapse.Outbox.EntityFrameworkCore/OutboxEntityTypeConfiguration.cs`
- Create: `src/Synapse.Outbox.EntityFrameworkCore/OutboxDbContext.cs`
- Modify: `Directory.Packages.props`
- Modify: `Synapse.slnx`
- Test: `test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj` (new project), `test/Synapse.Outbox.EntityFrameworkCore.Tests/OutboxEntityTypeConfigurationTests.cs`

**Interfaces:**
- Consumes: `UnambitiousFx.Synapse.Abstractions.IEvent` (only referenced by later tasks, not this one).
- Produces: `public sealed class OutboxEntity` (properties: `Id, EventType, Payload, Headers, CreatedAt, ProcessedAt, Processed, DeadLetter, Discarded, Attempts, LastError, NextAttemptAt`), `public sealed class OutboxEntityTypeConfiguration(string schema = "outbox") : IEntityTypeConfiguration<OutboxEntity>`, `public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)` with `DbSet<OutboxEntity> OutboxEvents` — Task 3 builds `EfCoreEventOutboxStorage<TContext>` against `OutboxEntity` and `_context.Set<OutboxEntity>()`.

- [ ] **Step 1: Add EF Core package versions to `Directory.Packages.props`**

Add to the `net10.0`-conditioned `ItemGroup`:

```xml
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.12" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.12" />
```

Add to the `net9.0`-conditioned `ItemGroup`:

```xml
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="9.0.20" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="9.0.20" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.20" />
```

Add to the `net8.0`-conditioned `ItemGroup`:

```xml
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="8.0.31" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="8.0.31" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.31" />
```

- [ ] **Step 2: Create the project file**

`src/Synapse.Outbox.EntityFrameworkCore/Synapse.Outbox.EntityFrameworkCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFrameworks>$(LibraryTargetFrameworks)</TargetFrameworks>
        <!-- EF Core's dynamic model building and reflection-based JSON (de)serialization of arbitrary
             IEvent payload types are not AOT-safe without additional ceremony (compiled models,
             per-type JsonSerializerContext) that is out of scope for this package. -->
        <IsAotCompatible>false</IsAotCompatible>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="..\Synapse.Abstractions\Synapse.Abstractions.csproj" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.EntityFrameworkCore" />
        <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    </ItemGroup>

</Project>
```

- [ ] **Step 3: Create `OutboxEntity`**

`src/Synapse.Outbox.EntityFrameworkCore/OutboxEntity.cs`:

```csharp
namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     EF Core row shape for a stored outbox entry. Public so it can be queried directly or mapped
///     inside a caller-owned <see cref="Microsoft.EntityFrameworkCore.DbContext" /> via
///     <see cref="OutboxEntityTypeConfiguration" />; callers that only need the outbox pattern's public
///     contract should still prefer <see cref="Abstractions.IEventOutboxStorage" /> and
///     <see cref="Abstractions.OutboxEntry" /> over querying this type directly.
/// </summary>
public sealed class OutboxEntity
{
    /// <summary>The stable identity of the stored item.</summary>
    public Guid Id { get; set; }

    /// <summary>The stored event's <see cref="Type.AssemblyQualifiedName" />, used to rehydrate it.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The event payload, serialized as JSON.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>The propagation headers captured at store time, serialized as JSON.</summary>
    public string Headers { get; set; } = string.Empty;

    /// <summary>When the entry was stored.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the entry was marked processed, if it was.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Whether the entry has been successfully dispatched.</summary>
    public bool Processed { get; set; }

    /// <summary>Whether the entry has exhausted its retries and moved to the dead-letter queue.</summary>
    public bool DeadLetter { get; set; }

    /// <summary>Whether the entry was taken back via <see cref="Abstractions.IDiscardableOutboxStorage" />.</summary>
    public bool Discarded { get; set; }

    /// <summary>The number of failed dispatch attempts recorded so far.</summary>
    public int Attempts { get; set; }

    /// <summary>The reason recorded for the most recent failure, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>When the entry becomes eligible for its next attempt, if it is currently backing off.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
```

- [ ] **Step 4: Create `OutboxEntityTypeConfiguration`**

`src/Synapse.Outbox.EntityFrameworkCore/OutboxEntityTypeConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Maps <see cref="OutboxEntity" /> to a table. Apply this inside your own
///     <see cref="DbContext" />'s <c>OnModelCreating</c> to keep the outbox table under your own
///     context's migrations, instead of registering the standalone <see cref="OutboxDbContext" />.
/// </summary>
/// <param name="schema">
///     The schema the outbox table is created under. Defaults to <c>"outbox"</c> so it does not
///     collide with an application's own tables regardless of which context it is applied to.
/// </param>
public sealed class OutboxEntityTypeConfiguration(string schema = "outbox")
    : IEntityTypeConfiguration<OutboxEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxEntity> builder)
    {
        builder.ToTable("outbox_events", schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired();
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.Headers).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => new { e.Processed, e.DeadLetter, e.Discarded, e.NextAttemptAt })
            .HasDatabaseName("ix_outbox_events_pending");
        builder.HasIndex(e => e.DeadLetter)
            .HasDatabaseName("ix_outbox_events_dead_letter");
    }
}
```

- [ ] **Step 5: Create `OutboxDbContext`**

`src/Synapse.Outbox.EntityFrameworkCore/OutboxDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Ready-made standalone <see cref="DbContext" /> holding only the outbox table. Register it with
///     your provider of choice; generate its migrations separately from your business context(s), e.g.
///     <c>dotnet ef migrations add InitialOutbox --context OutboxDbContext</c>.
/// </summary>
/// <remarks>
///     Prefer applying <see cref="OutboxEntityTypeConfiguration" /> inside your own
///     <see cref="DbContext" /> instead when you want the outbox table under your own context's
///     migration history rather than a separate one.
/// </remarks>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    /// <summary>The stored outbox entries.</summary>
    public DbSet<OutboxEntity> OutboxEvents => Set<OutboxEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());
    }
}
```

- [ ] **Step 6: Add both projects to the solution**

In `Synapse.slnx`, inside the `<Folder Name="/src/">` block, add:

```xml
        <Project Path="src\Synapse.Outbox.EntityFrameworkCore\Synapse.Outbox.EntityFrameworkCore.csproj" />
```

Inside the `<Folder Name="/test/">` block, add:

```xml
        <Project Path="test\Synapse.Outbox.EntityFrameworkCore.Tests\Synapse.Outbox.EntityFrameworkCore.Tests.csproj" />
```

- [ ] **Step 7: Create the test project**

`test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <ItemGroup>
        <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\Synapse.Outbox.EntityFrameworkCore\Synapse.Outbox.EntityFrameworkCore.csproj" />
    </ItemGroup>

</Project>
```

- [ ] **Step 8: Write a failing test proving the mapping works standalone**

`test/Synapse.Outbox.EntityFrameworkCore.Tests/OutboxEntityTypeConfigurationTests.cs`:

```csharp
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(OutboxDbContext))]
public sealed class OutboxEntityTypeConfigurationTests
{
    [Fact]
    public async Task OutboxDbContext_AddAndSaveEntity_PersistsAndReadsBack()
    {
        // Arrange (Given)
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new OutboxDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var entity = new OutboxEntity
        {
            Id = Guid.NewGuid(),
            EventType = "System.String",
            Payload = "\"hello\"",
            Headers = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act (When)
        context.OutboxEvents.Add(entity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var reloaded = await context.OutboxEvents
            .AsNoTracking()
            .SingleAsync(e => e.Id == entity.Id, TestContext.Current.CancellationToken);
        Assert.Equal(entity.Payload, reloaded.Payload);

        await connection.CloseAsync();
    }
}
```

Note: `Microsoft.Data.Sqlite.SqliteConnection` comes transitively from `Microsoft.EntityFrameworkCore.Sqlite` — no extra package reference needed.

- [ ] **Step 9: Run test to verify it fails**

Run: `dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f net10.0 --filter-class "*OutboxEntityTypeConfigurationTests"`

Expected: FAIL to build — the test project and its `ProjectReference` exist but nothing has broken yet at this point in a fresh scaffold; if Steps 1-6 were done correctly this test should actually already compile and PASS once the mapping code exists. Treat this step as the build+run check that proves Steps 1-6 produced a working, queryable `OutboxDbContext`; if it fails, fix the scaffold before proceeding — do not move to Task 3 with a broken foundation.

- [ ] **Step 10: Run test to verify it passes**

Run: `dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f net10.0 --filter-class "*OutboxEntityTypeConfigurationTests"`

Expected: PASS.

- [ ] **Step 11: Build the whole solution to confirm nothing else broke**

Run: `dotnet build Synapse.slnx`

Expected: builds with 0 errors, 0 warnings.

- [ ] **Step 12: Commit**

```bash
git add Directory.Packages.props Synapse.slnx src/Synapse.Outbox.EntityFrameworkCore test/Synapse.Outbox.EntityFrameworkCore.Tests
git commit -m "feat(outbox-efcore): scaffold OutboxEntity, mapping, and OutboxDbContext (#92)"
```

---

### Task 3: `EfCoreEventOutboxStorage<TContext>` and DI registration

**Files:**
- Create: `src/Synapse.Outbox.EntityFrameworkCore/EfCoreEventOutboxStorage.cs`
- Create: `src/Synapse.Outbox.EntityFrameworkCore/ServiceCollectionExtensions.cs`
- Modify: `src/Synapse.Outbox.EntityFrameworkCore/Synapse.Outbox.EntityFrameworkCore.csproj` (add `ProjectReference` to `Synapse.Abstractions` is already present from Task 2; add `Microsoft.Extensions.DependencyInjection.Abstractions` package reference)
- Modify: `Directory.Packages.props` (add `Microsoft.Extensions.DependencyInjection.Abstractions` per-TFM, matching the existing `Microsoft.Extensions.DependencyInjection` versions already pinned for net8/9/10)
- Test: `test/Synapse.Outbox.EntityFrameworkCore.Tests/EfCoreEventOutboxStorageTests.cs`, `test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/OutboxTestEvent.cs`, `test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/SqliteInMemoryDatabase.cs`

**Interfaces:**
- Consumes: `OutboxEntity`, `OutboxDbContext` (Task 2); `IEventOutboxStorage`, `IDiscardableOutboxStorage`, `OutboxEntry`, `IEvent` (`Synapse.Abstractions`, pre-existing).
- Produces: `public sealed class EfCoreEventOutboxStorage<TContext> : IEventOutboxStorage, IDiscardableOutboxStorage where TContext : DbContext`, constructed with `TContext context`; `public static class ServiceCollectionExtensions` with `AddEfCoreEventOutbox<TContext>()`. Task 4's transaction tests and Task 5's docs both construct `EfCoreEventOutboxStorage<TContext>` directly.

- [ ] **Step 1: Add `Microsoft.Extensions.DependencyInjection.Abstractions` package version**

Check the existing per-TFM `Microsoft.Extensions.DependencyInjection` version already in
`Directory.Packages.props` (net8.0 → `8.0.1`, net9.0 → `9.0.20`, net10.0 → `10.0.11`) and add the
matching `.Abstractions` package at the same versions to each of the three conditioned `ItemGroup`s:

```xml
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.1" />
```
```xml
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.20" />
```
```xml
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.11" />
```

(placed alongside each TFM's existing `Microsoft.Extensions.DependencyInjection` line).

- [ ] **Step 2: Add the package reference to the project**

In `src/Synapse.Outbox.EntityFrameworkCore/Synapse.Outbox.EntityFrameworkCore.csproj`, add to the
existing `PackageReference` `ItemGroup`:

```xml
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
```

- [ ] **Step 3: Create the test support types**

`test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/OutboxTestEvent.cs`:

```csharp
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

public sealed record OutboxTestEvent(string Name) : IEvent;
```

`test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/SqliteInMemoryDatabase.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

/// <summary>
///     A shared-cache SQLite in-memory database that stays alive for as long as one connection to it
///     is kept open, so multiple <see cref="OutboxDbContext"/> instances (or connections) can see the
///     same data — used to prove real transactional isolation without an external database.
/// </summary>
public sealed class SqliteInMemoryDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _keepAliveConnection;

    public string ConnectionString { get; }

    private SqliteInMemoryDatabase(string connectionString, SqliteConnection keepAliveConnection)
    {
        ConnectionString = connectionString;
        _keepAliveConnection = keepAliveConnection;
    }

    public static async Task<SqliteInMemoryDatabase> CreateAsync(CancellationToken cancellationToken)
    {
        var name = $"outbox-tests-{Guid.NewGuid():N}";
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(cancellationToken);

        var database = new SqliteInMemoryDatabase(connectionString, keepAlive);
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync(cancellationToken);
        return database;
    }

    public OutboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        return new OutboxDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _keepAliveConnection.DisposeAsync();
    }
}
```

- [ ] **Step 4: Write the failing tests**

`test/Synapse.Outbox.EntityFrameworkCore.Tests/EfCoreEventOutboxStorageTests.cs`:

```csharp
using JetBrains.Annotations;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(EfCoreEventOutboxStorage<OutboxDbContext>))]
public sealed class EfCoreEventOutboxStorageTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task AddAsync_ThenGetPendingEventsAsync_ReturnsTheStoredEvent()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);

        // Act (When)
        var addResult = await storage.AddAsync(new OutboxTestEvent("stored"), NoHeaders,
            TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(addResult.IsSuccess);
        var entry = Assert.Single(pending);
        var @event = Assert.IsType<OutboxTestEvent>(entry.Event);
        Assert.Equal("stored", @event.Name);
    }

    [Fact]
    public async Task AddAsync_WithHeaders_SurfacesThemOnThePendingEntry()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        // Act (When)
        await storage.AddAsync(new OutboxTestEvent("with-headers"), headers,
            TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var entry = Assert.Single(pending);
        Assert.Equal(headers["traceparent"], entry.Headers["traceparent"]);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_RemovesEntryFromPending()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("to-process"), NoHeaders,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(stored.Id, TestContext.Current.CancellationToken);
        var pendingAfter = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Empty(pendingAfter);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithUnknownId_ReturnsFailure()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task MarkAsFailedAsync_NotDeadLetter_SchedulesNextAttemptAndIncrementsAttempts()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("retry-me"), NoHeaders,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));
        var nextAttempt = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act (When)
        var result = await storage.MarkAsFailedAsync(stored.Id, "transient failure", deadLetter: false,
            nextAttemptAt: nextAttempt, cancellationToken: TestContext.Current.CancellationToken);
        var attempts = await storage.GetAttemptCountAsync(stored.Id, TestContext.Current.CancellationToken);
        var retrying = await storage.GetRetryingCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, attempts);
        Assert.Equal(1, retrying);
    }

    [Fact]
    public async Task MarkAsFailedAsync_DeadLetter_MovesEntryToDeadLetterQueue()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("doomed"), NoHeaders,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));

        // Act (When)
        await storage.MarkAsFailedAsync(stored.Id, "exhausted retries", deadLetter: true,
            cancellationToken: TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);
        var deadLetter = await storage.GetDeadLetterEventsAsync(TestContext.Current.CancellationToken);
        var deadLetterCount = await storage.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Empty(pending);
        Assert.Single(deadLetter);
        Assert.Equal(1, deadLetterCount);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithNoPendingEntries_ReturnsNull()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Null(age);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithPendingEntry_ReturnsNonNegativeAge()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("aging"), NoHeaders,
            TestContext.Current.CancellationToken);

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(age);
        Assert.True(age.Value >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ClearAsync_RemovesEverything()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("one"), NoHeaders, TestContext.Current.CancellationToken);
        await storage.AddAsync(new OutboxTestEvent("two"), NoHeaders, TestContext.Current.CancellationToken);

        // Act (When)
        var result = await storage.ClearAsync(TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);
        var count = await storage.GetPendingCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Empty(pending);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DiscardAsync_MatchingPendingEvent_RemovesItFromPending()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var @event = new OutboxTestEvent("to-discard");
        await storage.AddAsync(@event, NoHeaders, TestContext.Current.CancellationToken);

        // Act (When)
        var result = await storage.DiscardAsync([@event], TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task DiscardAsync_EventThisInstanceNeverStored_IsIgnored()
    {
        // Arrange (Given) — matched by reference: an event this storage instance never added is not
        // a match for any row, even if a value-equal one exists.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("kept"), NoHeaders, TestContext.Current.CancellationToken);
        var unrelatedEvent = new OutboxTestEvent("kept"); // value-equal, different reference

        // Act (When)
        var result = await storage.DiscardAsync([unrelatedEvent], TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Single(pending);
    }

    [Fact]
    public async Task DiscardAsync_AlreadyProcessedEvent_IsUntouched()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var @event = new OutboxTestEvent("already-done");
        await storage.AddAsync(@event, NoHeaders, TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));
        await storage.MarkAsProcessedAsync(stored.Id, TestContext.Current.CancellationToken);

        // Act (When)
        var result = await storage.DiscardAsync([@event], TestContext.Current.CancellationToken);
        var attempts = await storage.GetAttemptCountAsync(stored.Id, TestContext.Current.CancellationToken);

        // Assert (Then) — the row still exists and is unaffected (not re-queryable via pending, but
        // GetAttemptCountAsync finds it by id regardless of status, proving it was not deleted/altered)
        Assert.True(result.IsSuccess);
        Assert.Equal(0, attempts);
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f net10.0 --filter-class "*EfCoreEventOutboxStorageTests"`

Expected: build error — `EfCoreEventOutboxStorage<TContext>` does not exist yet.

- [ ] **Step 6: Implement `EfCoreEventOutboxStorage<TContext>`**

`src/Synapse.Outbox.EntityFrameworkCore/EfCoreEventOutboxStorage.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Persists outbox entries through an EF Core <see cref="DbContext" />, so a store lands in
///     whatever transaction that context is already enlisted in.
/// </summary>
/// <remarks>
///     <para>
///         Register this type Scoped — the default when wired via
///         <c>ISynapseConfig.SetEventOutboxStorage&lt;EfCoreEventOutboxStorage&lt;TContext&gt;&gt;()</c> —
///         and constructed with the same <typeparamref name="TContext" /> instance the rest of the
///         current scope uses. That is what lets a stored entry share the caller's transaction: this
///         storage never opens a transaction of its own, it only calls
///         <see cref="DbContext.SaveChangesAsync(CancellationToken)" /> on the context it was given.
///     </para>
///     <para>
///         Sharing atomicity across several <see cref="DbContext" />s (a modular monolith with one
///         business context per module plus this one) is done by sharing one <c>DbTransaction</c>
///         across them via <c>Database.UseTransactionAsync</c> before either context saves.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The <see cref="DbContext" /> type that owns the outbox table.</typeparam>
public sealed class EfCoreEventOutboxStorage<TContext> : IEventOutboxStorage, IDiscardableOutboxStorage
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly Dictionary<IEvent, Guid> _storedByReference = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     Initializes the storage with the <see cref="DbContext" /> whose transaction outbox writes
    ///     enlist in.
    /// </summary>
    /// <param name="context">The EF Core context that owns the outbox table.</param>
    public EfCoreEventOutboxStorage(TContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async ValueTask<Result> AddAsync<TEvent>(TEvent @event,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var entity = new OutboxEntity
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, typeof(TEvent)),
            Headers = JsonSerializer.Serialize(headers),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Set<OutboxEntity>().Add(entity);
        var saveResult = await TrySaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult;
        }

        _storedByReference[@event] = entity.Id;
        return Result.Success();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await _context.Set<OutboxEntity>()
            .Where(e => !e.Processed && !e.DeadLetter && !e.Discarded &&
                        (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(ToOutboxEntry).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<Result> MarkAsProcessedAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<OutboxEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure($"Outbox item '{id}' was not found in the outbox storage");
        }

        entity.Processed = true;
        entity.ProcessedAt = DateTimeOffset.UtcNow;
        entity.LastError = null;
        entity.NextAttemptAt = null;

        return await TrySaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Set<OutboxEntity>().ExecuteDeleteAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result> MarkAsFailedAsync(Guid id,
        string reason,
        bool deadLetter,
        DateTimeOffset? nextAttemptAt = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<OutboxEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure($"Outbox item '{id}' was not found in the outbox storage");
        }

        entity.Attempts++;
        entity.LastError = reason;
        if (deadLetter)
        {
            entity.DeadLetter = true;
            entity.NextAttemptAt = null;
        }
        else
        {
            entity.NextAttemptAt = nextAttemptAt;
        }

        return await TrySaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Set<OutboxEntity>().Where(e => e.DeadLetter).ToListAsync(cancellationToken);
        return rows.Select(ToOutboxEntry).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<int?> GetAttemptCountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<OutboxEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entity?.Attempts;
    }

    /// <inheritdoc />
    public async ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<OutboxEntity>()
            .CountAsync(e => !e.Processed && !e.DeadLetter && !e.Discarded, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<OutboxEntity>()
            .CountAsync(e => !e.Processed && !e.DeadLetter && !e.Discarded && e.Attempts > 0, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<OutboxEntity>().CountAsync(e => e.DeadLetter, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
    {
        var oldest = await _context.Set<OutboxEntity>()
            .Where(e => !e.Processed && !e.DeadLetter && !e.Discarded)
            .OrderBy(e => e.CreatedAt)
            .Select(e => (DateTimeOffset?)e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return oldest is null ? null : DateTimeOffset.UtcNow - oldest.Value;
    }

    /// <inheritdoc />
    public async ValueTask<Result> DiscardAsync(IReadOnlyCollection<IEvent> events,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>();
        foreach (var @event in events)
        {
            if (_storedByReference.TryGetValue(@event, out var id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return Result.Success();
        }

        var entities = await _context.Set<OutboxEntity>()
            .Where(e => ids.Contains(e.Id) && !e.Processed && !e.DeadLetter)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            entity.Discarded = true;
        }

        return await TrySaveChangesAsync(cancellationToken);
    }

    private async ValueTask<Result> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    private static OutboxEntry ToOutboxEntry(OutboxEntity entity)
    {
        var type = Type.GetType(entity.EventType, throwOnError: true)!;
        var @event = (IEvent)JsonSerializer.Deserialize(entity.Payload, type)!;
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Headers)
                      ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new OutboxEntry(entity.Id, @event, headers);
    }
}
```

- [ ] **Step 7: Implement `ServiceCollectionExtensions`**

`src/Synapse.Outbox.EntityFrameworkCore/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     DI registration helpers for the EF Core outbox storage.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="EfCoreEventOutboxStorage{TContext}" /> Scoped for the given context
    ///     type. Call
    ///     <c>ISynapseConfig.SetEventOutboxStorage&lt;EfCoreEventOutboxStorage&lt;TContext&gt;&gt;()</c>
    ///     inside <c>AddSynapse</c> to wire it as the active outbox storage.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext" /> type that owns the outbox table.</typeparam>
    public static IServiceCollection AddEfCoreEventOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        return services.AddScoped<EfCoreEventOutboxStorage<TContext>>();
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f net10.0 --filter-class "*EfCoreEventOutboxStorageTests"`

Expected: PASS, all cases in Step 4.

- [ ] **Step 9: Run every test in the project, all three TFMs**

Run for each of `net8.0`, `net9.0`, `net10.0`:
`dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f <tfm>`

Expected: PASS on all three.

- [ ] **Step 10: Build the whole solution**

Run: `dotnet build Synapse.slnx`

Expected: 0 errors, 0 warnings.

- [ ] **Step 11: Commit**

```bash
git add Directory.Packages.props src/Synapse.Outbox.EntityFrameworkCore test/Synapse.Outbox.EntityFrameworkCore.Tests
git commit -m "feat(outbox-efcore): EfCoreEventOutboxStorage and DI registration helper (#92)"
```

---

### Task 4: Transactional proof tests — rollback, isolation, multi-`DbContext` sharing

**Files:**
- Create: `test/Synapse.Outbox.EntityFrameworkCore.Tests/EfCoreEventOutboxStorageTransactionTests.cs`
- Create: `test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/BusinessDbContext.cs`, `test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/BusinessRecord.cs`

**Interfaces:**
- Consumes: `EfCoreEventOutboxStorage<TContext>`, `OutboxDbContext`, `SqliteInMemoryDatabase` (Tasks 2-3).
- Produces: nothing new consumed by later tasks — this task is the spec's core "prove it's really transactional" requirement, standalone.

- [ ] **Step 1: Create a second, unrelated `DbContext` + entity to stand in for "the caller's business data"**

`test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/BusinessRecord.cs`:

```csharp
namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

public sealed class BusinessRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

`test/Synapse.Outbox.EntityFrameworkCore.Tests/Support/BusinessDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

public sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<BusinessRecord> Records => Set<BusinessRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessRecord>(b =>
        {
            b.ToTable("business_records");
            b.HasKey(r => r.Id);
        });
    }
}
```

- [ ] **Step 2: Write the failing tests**

`test/Synapse.Outbox.EntityFrameworkCore.Tests/EfCoreEventOutboxStorageTransactionTests.cs`:

```csharp
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(EfCoreEventOutboxStorage<OutboxDbContext>))]
public sealed class EfCoreEventOutboxStorageTransactionTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task AddAsync_TransactionRolledBack_RemovesThePendingEntry()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("rolled-back"), NoHeaders,
            TestContext.Current.CancellationToken);

        // Act (When)
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — a fresh context on the same database sees nothing: the row never committed
        await using var verifyContext = database.CreateContext();
        var count = await verifyContext.OutboxEvents.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddAsync_UncommittedTransaction_IsInvisibleToAConcurrentConnection()
    {
        // Arrange (Given) — two separate connections to the same shared-cache database, simulating
        // two concurrent sessions.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var writerContext = database.CreateContext();
        await using var transaction = await writerContext.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(writerContext);
        await storage.AddAsync(new OutboxTestEvent("in-flight"), NoHeaders,
            TestContext.Current.CancellationToken);

        // Act (When) — a second, independent connection reads before the writer commits
        await using var readerContext = database.CreateContext();
        var countBeforeCommit = await readerContext.OutboxEvents.CountAsync(
            TestContext.Current.CancellationToken);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        await using var readerContextAfterCommit = database.CreateContext();
        var countAfterCommit = await readerContextAfterCommit.OutboxEvents.CountAsync(
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(0, countBeforeCommit);
        Assert.Equal(1, countAfterCommit);
    }

    [Fact]
    public async Task TwoDbContextsSharingOneTransaction_CommitTogether()
    {
        // Arrange (Given) — a business context and the outbox context share one DbTransaction, the
        // pattern the issue asked to be proven: Database.UseTransactionAsync.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        var businessOptions = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(database.ConnectionString)
            .Options;
        await using var businessContext = new BusinessDbContext(businessOptions);
        await businessContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await using var outboxContext = database.CreateContext();

        await using var transaction = await businessContext.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await outboxContext.Database.UseTransactionAsync(transaction.GetDbTransaction(),
            TestContext.Current.CancellationToken);

        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(outboxContext);
        var recordId = Guid.NewGuid();

        // Act (When)
        businessContext.Records.Add(new BusinessRecord { Id = recordId, Name = "widget" });
        await businessContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await storage.AddAsync(new OutboxTestEvent("widget-created"), NoHeaders,
            TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        await using var verifyBusiness = new BusinessDbContext(businessOptions);
        await using var verifyOutbox = database.CreateContext();
        Assert.Equal(1, await verifyBusiness.Records.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verifyOutbox.OutboxEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoDbContextsSharingOneTransaction_RollBackTogether()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        var businessOptions = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(database.ConnectionString)
            .Options;
        await using var businessContext = new BusinessDbContext(businessOptions);
        await businessContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await using var outboxContext = database.CreateContext();

        await using var transaction = await businessContext.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await outboxContext.Database.UseTransactionAsync(transaction.GetDbTransaction(),
            TestContext.Current.CancellationToken);

        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(outboxContext);

        // Act (When)
        businessContext.Records.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = "doomed-widget" });
        await businessContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await storage.AddAsync(new OutboxTestEvent("doomed-widget-created"), NoHeaders,
            TestContext.Current.CancellationToken);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — neither the business row nor the outbox row exist: they rolled back together
        await using var verifyBusiness = new BusinessDbContext(businessOptions);
        await using var verifyOutbox = database.CreateContext();
        Assert.Equal(0, await verifyBusiness.Records.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verifyOutbox.OutboxEvents.CountAsync(TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail (or pass unexpectedly)**

Run: `dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f net10.0 --filter-class "*EfCoreEventOutboxStorageTransactionTests"`

Expected: build succeeds (all production types already exist from Task 3) and these tests should
already PASS if Task 3's implementation is correct, since no new production code is introduced in
this task — it is a pure verification task. Treat any failure here as a real defect in Task 3's
`EfCoreEventOutboxStorage<TContext>` (most likely: `AddAsync` opening its own transaction instead of
using the ambient one, or `SaveChangesAsync` not actually being called before the caller commits) and
fix it in `src/Synapse.Outbox.EntityFrameworkCore/EfCoreEventOutboxStorage.cs` before proceeding —
do not weaken these tests to make them pass.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f net10.0 --filter-class "*EfCoreEventOutboxStorageTransactionTests"`

Expected: PASS, all 4 tests.

- [ ] **Step 5: Run the full test project on all three TFMs**

Run for each of `net8.0`, `net9.0`, `net10.0`:
`dotnet test --project test/Synapse.Outbox.EntityFrameworkCore.Tests/Synapse.Outbox.EntityFrameworkCore.Tests.csproj -f <tfm>`

Expected: PASS on all three.

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build Synapse.slnx`

Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add test/Synapse.Outbox.EntityFrameworkCore.Tests
git commit -m "test(outbox-efcore): prove rollback, isolation, and multi-DbContext transaction sharing (#92)"
```

---

### Task 5: Docs and changelog

**Files:**
- Modify: `docs/docs/outbox.mdx`
- Create: `docs/docs/outbox-entityframeworkcore.mdx`
- Create: `docs/known-issues/070-seteventoutboxstorage-always-registers-singleton.md`
- Modify: `docs/known-issues/README.md`
- Modify: `docs/docs/changelog.mdx`

**Interfaces:**
- Consumes: nothing (docs only).
- Produces: nothing (terminal task).

- [ ] **Step 1: Replace the DIY EF Core sample in `docs/docs/outbox.mdx`**

Find the "Replace the storage for production" section (containing `cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage>();` and the `public class EfCoreEventOutboxStorage : IEventOutboxStorage` sample). Replace the whole code sample and the paragraph introducing it with:

```markdown
## Replace the storage for production

The built-in `InMemoryEventOutboxStorage` is suitable for testing and simple scenarios. For production, replace it with a persistent implementation. `UnambitiousFx.Synapse.Outbox.EntityFrameworkCore` ships a ready-made, transactional one — see [EF Core outbox storage](./outbox-entityframeworkcore) for installation, the multi-`DbContext` (modular monolith) transaction-sharing pattern, and migrations. In short:

```csharp
services.AddDbContext<OutboxDbContext>(o => o.UseNpgsql(connectionString));
services.AddEfCoreEventOutbox<OutboxDbContext>();
services.AddSynapse(cfg =>
{
    cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<OutboxDbContext>>();
});
```
```

- [ ] **Step 2: Write `docs/docs/outbox-entityframeworkcore.mdx`**

```markdown
---
sidebar_position: 10.5
title: EF Core Outbox Storage
description: A transactional, multi-DbContext-friendly IEventOutboxStorage backed by EF Core.
---

# EF Core Outbox Storage

`UnambitiousFx.Synapse.Outbox.EntityFrameworkCore` is a transactional `IEventOutboxStorage`
implementation backed by EF Core. It writes outbox entries through whatever `DbContext` it is given,
so a stored entry lands in that context's ambient transaction — the same guarantee `EmitMode.Outbox`
exists for.

## Install

```bash
dotnet add package UnambitiousFx.Synapse.Outbox.EntityFrameworkCore
```

## Choose a schema story

### Standalone `OutboxDbContext` (the common case)

```csharp
services.AddDbContext<OutboxDbContext>(o => o.UseNpgsql(connectionString));
services.AddEfCoreEventOutbox<OutboxDbContext>();
services.AddSynapse(cfg =>
{
    cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<OutboxDbContext>>();
});
```

Generate its migrations like any other context, in your own project:

```bash
dotnet ef migrations add InitialOutbox --context OutboxDbContext
```

### Embed the table in your own `DbContext`

If you'd rather keep the outbox table under your existing context's migration history instead of a
separate one, apply `OutboxEntityTypeConfiguration` yourself:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());
        // ... your own entities
    }
}
```

```csharp
services.AddEfCoreEventOutbox<AppDbContext>();
services.AddSynapse(cfg =>
{
    cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<AppDbContext>>();
});
```

`OutboxEntityTypeConfiguration`'s constructor takes an optional `schema` parameter (default
`"outbox"`), so the table never collides with your own tables regardless of which context it lives
in.

## Multi-`DbContext` (modular monolith)

When a business `DbContext` and the outbox context must commit or roll back together — the point of
the outbox pattern — share one `DbTransaction` across them with `Database.UseTransactionAsync`:

```csharp
await using var tx = await businessContext.Database.BeginTransactionAsync(ct);
await outboxContext.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);

businessContext.Tasks.Add(task);
await businessContext.SaveChangesAsync(ct);                                    // business save

await emitter.EmitAsync(new TaskCreatedEvent(task.Id), EmitMode.Outbox, ct);    // outbox write,
                                                                                  // same DbTransaction

await tx.CommitAsync(ct);   // both commit together; either rolls back together
```

See also [Modular Monolith](./modular-monolith) for the broader module-boundary picture this fits
into.

## Limitations

`GetPendingEventsAsync` has no row-locking. Running more than one instance of the process that calls
`IOutboxCommit.CommitAsync()` (or any other caller of `IOutboxManager.ProcessPendingAsync`) against
the same EF Core storage can double-dispatch a pending entry — the same limitation the in-memory
storage already has today. A `FOR UPDATE SKIP LOCKED`-based (or equivalent) claiming dispatcher is
tracked as a follow-up.

## See also

- [Outbox Pattern](./outbox) — the general outbox contract, retry/dead-letter behavior, health check.
- [Modular Monolith](./modular-monolith) — module boundaries this storage's multi-`DbContext` support
  is designed for.
```

- [ ] **Step 3: Verify next known-issue id**

Run: `ls docs/known-issues/ | grep -oP '^\d+' | sort -n | tail -1`

Expected output: `069` — confirms `070` is the next free id (already reflected in Step 4's filename;
if a different number comes back because another issue landed on `main` first, rename the file
accordingly and use that number in Steps 4-6 instead).

- [ ] **Step 4: Write the known-issue detail file**

`docs/known-issues/070-seteventoutboxstorage-always-registers-singleton.md`:

```markdown
# [Bug]: SetEventOutboxStorage always registers the storage as Singleton, breaking scoped dependencies

**Severity:** Medium
**Area:** Core DI
**Discovered on:** `feature/ef-core-outbox`, .NET 10
**Status:** ✅ **Resolved** on `feature/ef-core-outbox` — see [Resolution](#resolution).

> **TL;DR.** `ISynapseConfig.SetEventOutboxStorage<T>()` registered `T` as `IEventOutboxStorage` with
> `services.AddSingleton(...)` unconditionally, so any custom storage with a scoped dependency (most
> notably a `DbContext`) either threw at resolution time under validated scopes, or silently shared
> one instance across every request. `SetEventOutboxStorage<T>()` now takes an optional
> `ServiceLifetime` parameter, defaulting to `Scoped`.

---

## Describe the bug

`docs/docs/outbox.mdx`'s own "Replace the storage for production" section showed a sample
`EfCoreEventOutboxStorage` constructed with a `DbContext`. Following that sample and registering it
via `cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage>()` produced a storage that was silently
unsafe (or outright broken under `ServiceProviderOptions.ValidateScopes = true`), because the
registration path always used `services.AddSingleton(...)` regardless of what the configured type's
own dependencies were.

---

## Steps to reproduce

1. Implement a custom `IEventOutboxStorage` that takes a `DbContext` (or any other scoped service) in
   its constructor.
2. Register it: `services.AddSynapse(cfg => cfg.SetEventOutboxStorage<MyStorage>());`.
3. Build the provider with `ServiceProviderOptions.ValidateScopes = true` and resolve
   `IEventOutboxStorage` from a scope.

---

## Expected behavior

The storage resolves per-scope, matching the lifetime of the `DbContext` (or other scoped
dependency) it depends on.

---

## Actual behavior

`InvalidOperationException: Cannot consume scoped service ... from singleton ...` when scope
validation is enabled; without validation, one instance — and its captured `DbContext` — is silently
shared across every request for the lifetime of the process, which is unsafe (`DbContext` is not
thread-safe) and defeats the point of enlisting in a per-request transaction.

---

## Code sample

```csharp
services.AddSynapse(cfg => cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<AppDbContext>>());
var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
using var scope = provider.CreateScope();
scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>(); // throws
```

---

## Library version

`feature/ef-core-outbox` (pre-release)

## .NET version

.NET 8 / 9 / 10

## Operating system

Any

---

## Additional context

### Root cause

`SynapseConfig.Apply()` called `services.AddSingleton(typeof(IEventOutboxStorage), _eventOutBoxStorage);`
unconditionally in `src/Synapse/SynapseConfig.cs`, regardless of whether `_eventOutBoxStorage` was the
default `InMemoryEventOutboxStorage` (correctly Singleton — it is deliberately shared, in-memory,
stateful across the whole process) or a caller-supplied type via `SetEventOutboxStorage<T>()`, whose
correct lifetime depends entirely on what `T` itself depends on.

### Resolution

`ISynapseConfig.SetEventOutboxStorage<TEventOutboxStorage>()` now takes an optional
`ServiceLifetime lifetime = ServiceLifetime.Scoped` parameter. `SynapseConfig` registers via
`services.Add(new ServiceDescriptor(typeof(IEventOutboxStorage), _eventOutBoxStorage, _eventOutBoxStorageLifetime))`,
where `_eventOutBoxStorageLifetime` defaults to `ServiceLifetime.Singleton` and is only overwritten
when `SetEventOutboxStorage<T>()` is actually called — so the untouched default path
(`InMemoryEventOutboxStorage`, nobody calling `SetEventOutboxStorage`) keeps its exact original
Singleton registration. A caller invoking `SetEventOutboxStorage<T>()` with no argument now gets
`Scoped`, correct for a `DbContext`-backed storage such as
`UnambitiousFx.Synapse.Outbox.EntityFrameworkCore`'s `EfCoreEventOutboxStorage<TContext>`; a caller
that needs `Singleton` for a stateless, thread-safe storage passes it explicitly.

**Verification.** `test/Synapse.Tests/SynapseConfigEventOutboxStorageLifetimeTests.cs` — asserts the
default path stays `InMemoryEventOutboxStorage`/`Singleton`, a no-argument `SetEventOutboxStorage<T>()`
registers `Scoped`, an explicit `ServiceLifetime.Singleton` is honored, and a scoped custom storage
resolves cleanly under `ValidateScopes = true`.
```

- [ ] **Step 5: Add the index row to `docs/known-issues/README.md`**

Append (in numeric order, after `069`):

```markdown
| [070](070-seteventoutboxstorage-always-registers-singleton.md) | SetEventOutboxStorage always registered the storage as Singleton, breaking scoped dependencies | ✅ Resolved | Medium | Core DI |
```

Update the discovery-range blockquote at the bottom of the file to include `070`.

- [ ] **Step 6: Add the row to `docs/docs/changelog.mdx`**

In the "Core DI" section's table, append:

```markdown
| [070](https://github.com/UnambitiousFx/Synapse/blob/feature/ef-core-outbox/docs/known-issues/070-seteventoutboxstorage-always-registers-singleton.md) | SetEventOutboxStorage always registered the storage as Singleton, breaking scoped dependencies | Medium |
```

(Replace `feature/ef-core-outbox` with the actual branch name used for this work if it differs.)

- [ ] **Step 7: Verify the docs build**

Run: `cd docs && pnpm build`

Expected: succeeds, zero broken-link warnings.

- [ ] **Step 8: Commit**

```bash
git add docs/docs/outbox.mdx docs/docs/outbox-entityframeworkcore.mdx docs/known-issues/070-seteventoutboxstorage-always-registers-singleton.md docs/known-issues/README.md docs/docs/changelog.mdx
git commit -m "docs(outbox-efcore): EF Core outbox storage guide + SetEventOutboxStorage known-issue (#92)"
```

---

## After all tasks: final review and merge

Per `superpowers:subagent-driven-development`: dispatch the final whole-branch code review (most
capable available model), address findings with one fix round + scoped re-review, then follow
`superpowers:finishing-a-development-branch` — push, open a PR against `main`, wait for CI green on
all three TFMs, squash-merge, close issue #92 with a summary comment, and file the two follow-up
issues named in the spec's "Follow-up issues to file after merge" section.
