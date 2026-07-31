using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Observability;
using UnambitiousFx.Synapse.Propagation;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

/// <summary>
///     Covers the store-then-dispatch hop, which is where a flow crosses from the request that caused it to the
///     work that results from it — the point at which the producing span has already ended.
/// </summary>
public sealed class OutboxFlowIdentityTests
{
    [Fact]
    public async Task StoreThenDispatch_PreservesTheOriginatingTraceId()
    {
        // Arrange (Given)
        using var probe = new ActivityProbe();
        var storage = new InMemoryEventOutboxStorage();

        // Read through Activity.Current: the dispatch activity is parented on the stored trace context, so this
        // is the mechanism that actually carries the flow across the hop rather than a mirror of it.
        var traceIdDuringDispatch = string.Empty;

        OutboxManager manager;
        ActivityTraceId producingTraceId;

        using (var producing = StartActivity("producing-request"))
        {
            Assert.NotNull(producing);
            producingTraceId = producing!.TraceId;

            // Built on the producing activity, which is what the context factory does whenever one exists: the
            // trace id the request logs is the trace id its span was recorded under.
            var context = NewContextOn(producing);
            context.SetBaggage("tenant.id", "contoso");

            manager = BuildManager(storage, (_, _, _) =>
            {
                traceIdDuringDispatch = Activity.Current?.TraceId.ToHexString() ?? string.Empty;
                return new ValueTask<Result>(Result.Success());
            }, new StubContextAccessor(context, true));

            await manager.StoreAsync(new EventExample("mail-me"), TestContext.Current.CancellationToken);
        }

        var stored = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();
        var storedBaggage = stored.Headers[PropagationKeys.Baggage];

        // Act (When) — dispatch runs after the producing request would normally have ended
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(producingTraceId.ToHexString(), traceIdDuringDispatch);
        Assert.Contains("tenant.id=contoso", storedBaggage, StringComparison.Ordinal);
        Assert.DoesNotContain("synapse.", storedBaggage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreThenDispatch_PutsTheDispatchSpanInTheProducingTrace()
    {
        // Arrange (Given) — the producing span has ended by dispatch time, but the trace continues, so the whole
        // flow shows up as one trace rather than two joined by a link
        using var probe = new ActivityProbe();
        var storage = new InMemoryEventOutboxStorage();

        OutboxManager manager;
        ActivityTraceId producingTraceId;
        ActivitySpanId producingSpanId;

        using (var producing = StartActivity("producing-request"))
        {
            Assert.NotNull(producing);
            producingTraceId = producing!.TraceId;
            producingSpanId = producing.SpanId;
            manager = BuildManager(storage, (_, _, _) => new ValueTask<Result>(Result.Success()),
                new StubContextAccessor(NewContextOn(producing), true));
            await manager.StoreAsync(new EventExample("mail-me"), TestContext.Current.CancellationToken);
        }

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var dispatchActivity = Assert.Single(probe.Started,
            a => a.OperationName == "synapse.outbox.dispatch");
        Assert.Equal(producingTraceId, dispatchActivity.TraceId);
        Assert.Equal(producingSpanId, dispatchActivity.ParentSpanId);
        Assert.Empty(dispatchActivity.Links);
    }

    [Fact]
    public async Task StoreThenDispatch_WithNoProducingActivity_StillContinuesTheContextsTrace()
    {
        // Arrange (Given) — nothing was recording when the entry was stored, so there is no span to continue. The
        // context still has a trace id, and that is what the stored headers must carry: without it the entry was
        // filed with no trace context at all and its dispatch became a disconnected root, which is precisely what
        // OutboxEntry.Headers exists to prevent (known issue 040).
        using var probe = new ActivityProbe();
        var storage = new InMemoryEventOutboxStorage();
        var context = NewContext();
        var manager = BuildManager(storage, (_, _, _) => new ValueTask<Result>(Result.Success()),
            new StubContextAccessor(context, true));

        await manager.StoreAsync(new EventExample("mail-me"), TestContext.Current.CancellationToken);
        var stored = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Contains(context.TraceId, stored.Headers[PropagationKeys.TraceParent], StringComparison.Ordinal);
        var dispatchActivity = Assert.Single(probe.Started,
            a => a.OperationName == "synapse.outbox.dispatch");
        Assert.Equal(context.TraceId, dispatchActivity.TraceId.ToHexString());
        Assert.NotEqual(default, dispatchActivity.ParentSpanId);
    }

    [Fact]
    public async Task StoreThenDispatch_WithNoContextAtAll_StartsItsOwnTrace()
    {
        // Arrange (Given) — an event stored outside any unit of work has no flow to belong to, so there is
        // genuinely nothing to continue and the dispatch is a root
        using var probe = new ActivityProbe();
        var storage = new InMemoryEventOutboxStorage();
        var manager = BuildManager(storage, (_, _, _) => new ValueTask<Result>(Result.Success()),
            new StubContextAccessor(null, false));

        await manager.StoreAsync(new EventExample("mail-me"), TestContext.Current.CancellationToken);

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var dispatchActivity = Assert.Single(probe.Started,
            a => a.OperationName == "synapse.outbox.dispatch");
        Assert.NotEqual(default, dispatchActivity.TraceId);
        Assert.Equal(default, dispatchActivity.ParentSpanId);
    }

    [Fact]
    public async Task Dispatch_WithCaseVariantDuplicateStoredHeaders_ProcessesTheWholeBatch()
    {
        // Arrange (Given) — IEventOutboxStorage is a public extension point, and one persisting headers in a
        // case-sensitive column can hand back both "traceparent" and "TraceParent". Building the carrier with
        // ToDictionary threw on the duplicate, from outside the try, so the entire batch aborted with nothing
        // marked processed or failed (known issue 041).
        var storage = new DuplicateHeaderOutboxStorage(
            new OutboxEntry(Guid.NewGuid(), new EventExample("dup"), new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                ["TraceParent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
            }),
            new OutboxEntry(Guid.NewGuid(), new EventExample("plain")));

        var dispatched = 0;
        var manager = BuildManager(storage, (_, _, _) =>
        {
            dispatched++;
            return new ValueTask<Result>(Result.Success());
        }, new StubContextAccessor(NewContext(), true));

        // Act (When)
        var result = await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — both entries dispatched, neither left in limbo
        Assert.True(result.IsSuccess);
        Assert.Equal(2, dispatched);
        Assert.Equal(2, storage.ProcessedIds.Count);
        Assert.Empty(storage.FailedIds);
    }

    [Fact]
    public async Task Dispatch_DoesNotLeakTheEntrysTraceIntoItsCaller()
    {
        // Arrange (Given) — an entry's flow is restored only for the duration of its dispatch. A processor
        // draining the outbox must come back out on the trace it went in on, whatever the entries carried.
        using var probe = new ActivityProbe();
        var storage = new InMemoryEventOutboxStorage();
        var manager = BuildManager(storage, (_, _, _) => new ValueTask<Result>(Result.Success()),
            new StubContextAccessor(NewContext(), true));

        using (var producing = StartActivity("producing-request"))
        {
            Assert.NotNull(producing);
            await manager.StoreAsync(new EventExample("mail-me"), TestContext.Current.CancellationToken);
        }

        using var processing = StartActivity("outbox-processor");
        Assert.NotNull(processing);
        var processorTraceId = processing!.TraceId;

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — the dispatch really did move to the entry's trace, and the caller is back on its own
        var dispatchActivity = Assert.Single(probe.Started,
            a => a.OperationName == "synapse.outbox.dispatch");
        Assert.NotEqual(processorTraceId, dispatchActivity.TraceId);
        Assert.Same(processing, Activity.Current);
        Assert.Equal(processorTraceId, Activity.Current!.TraceId);
    }

    [Fact]
    public async Task Dispatch_BuildsTheHandlersContextFromTheStoredEntry()
    {
        // Arrange (Given) — no ActivityListener is registered, so StartActivity returns null and the stored
        // trace context can only reach the handlers through the context they resolve. Applying it to the dispatch
        // span alone left them on the committing scope's context: a different trace id, and none of the baggage
        // the entry was filed with.
        var storage = new InMemoryEventOutboxStorage();
        var storing = NewContext();
        storing.SetBaggage("tenant.id", "contoso");

        await BuildManager(storage, NoOpDispatch, new StubContextAccessor(storing, true))
            .StoreAsync(new EventExample("mail-me"), TestContext.Current.CancellationToken);

        var committing = NewContext();
        IContext? dispatchContext = null;
        var manager = BuildManager(storage,
            (_, dispatcher, _) =>
            {
                dispatchContext = ((ScopeProbeDispatcher)dispatcher).Context;
                return new ValueTask<Result>(Result.Success());
            },
            new StubContextAccessor(committing, true),
            sp => new ScopeProbeDispatcher(sp.GetRequiredService<IContextAccessor>()));

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(dispatchContext);
        Assert.Equal(storing.TraceId, dispatchContext!.TraceId);
        Assert.NotEqual(committing.TraceId, dispatchContext.TraceId);
        Assert.Equal("contoso", dispatchContext.Baggage["tenant.id"]);
    }

    [Fact]
    public async Task Dispatch_OfAnEntryStoredByAnotherFlow_DoesNotAdoptTheCommittingFlowsIdentity()
    {
        // Arrange (Given) — pending entries are retrieved across scopes, so the canonical per-request commit
        // dispatches whatever any request stored. Each entry must be dispatched under its own flow; running them
        // in the committing scope gave every one of them that request's trace id and baggage.
        var storage = new InMemoryEventOutboxStorage();
        var first = NewContext();
        first.SetBaggage("tenant.id", "first");
        var second = NewContext();
        second.SetBaggage("tenant.id", "second");

        await BuildManager(storage, NoOpDispatch, new StubContextAccessor(first, true))
            .StoreAsync(new EventExample("for-first"), TestContext.Current.CancellationToken);
        await BuildManager(storage, NoOpDispatch, new StubContextAccessor(second, true))
            .StoreAsync(new EventExample("for-second"), TestContext.Current.CancellationToken);

        var seen = new List<(string TraceId, string Tenant)>();
        var manager = BuildManager(storage,
            (_, dispatcher, _) =>
            {
                var context = ((ScopeProbeDispatcher)dispatcher).Context;
                seen.Add((context.TraceId, context.Baggage["tenant.id"]));
                return new ValueTask<Result>(Result.Success());
            },
            new StubContextAccessor(NewContext(), true),
            sp => new ScopeProbeDispatcher(sp.GetRequiredService<IContextAccessor>()));

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — two dispatches, two flows, neither borrowed from the other or from the committer
        Assert.Equal(2, seen.Count);
        Assert.Contains((first.TraceId, "first"), seen);
        Assert.Contains((second.TraceId, "second"), seen);
    }

    private static ValueTask<Result> NoOpDispatch(IEvent @event,
        IEventDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        return new ValueTask<Result>(Result.Success());
    }

    private static Activity? StartActivity(string name)
    {
        return SynapseActivitySource.Source.StartActivity(name);
    }

    private static Context NewContext()
    {
        return new Context(
            new ContextIdentity(ActivityTraceId.CreateRandom().ToHexString(), null, DateTimeOffset.UtcNow));
    }

    /// <summary>
    ///     A context whose trace id is the activity's, which is what the context factory produces whenever an
    ///     activity exists.
    /// </summary>
    private static Context NewContextOn(Activity activity)
    {
        return new Context(new ContextIdentity(activity.TraceId.ToHexString(), null, DateTimeOffset.UtcNow));
    }

    private static OutboxManager BuildManager(IEventOutboxStorage storage,
        DispatchEventDelegate dispatcher,
        IContextAccessor contextAccessor,
        Func<IServiceProvider, IEventDispatcher>? scopedDispatcher = null)
    {
        var dispatcherOptions = new EventDispatcherOptions();
        dispatcherOptions.Dispatchers[typeof(EventExample)] = dispatcher;

        return new OutboxManager(
            storage,
            DispatchScopes.For(scopedDispatcher),
            Substitute.For<ISynapseMetrics>(),
            new W3CContextPropagator(),
            contextAccessor,
            Options.Create(dispatcherOptions),
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxManager>.Instance);
    }

    /// <summary>
    ///     Exposes the context of the scope it was resolved from, so a test can assert which context the code
    ///     inside the dispatch scope actually sees.
    /// </summary>
    private sealed class ScopeProbeDispatcher : IEventDispatcher
    {
        private readonly IContextAccessor _accessor;

        public ScopeProbeDispatcher(IContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public IContext Context => _accessor.Context;

        public ValueTask<Result> DispatchAsync<TEvent>(TEvent @event,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    /// <summary>
    ///     A storage whose entries are handed back exactly as given, including header keys that differ only by
    ///     case — what a persistent implementation using a case-sensitive column can produce.
    /// </summary>
    private sealed class DuplicateHeaderOutboxStorage : IEventOutboxStorage
    {
        private readonly OutboxEntry[] _entries;

        public DuplicateHeaderOutboxStorage(params OutboxEntry[] entries)
        {
            _entries = entries;
        }

        public List<Guid> ProcessedIds { get; } = [];

        public List<Guid> FailedIds { get; } = [];

        public ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IReadOnlyList<OutboxEntry>>(_entries);
        }

        public ValueTask<Result> MarkAsProcessedAsync(Guid id,
            CancellationToken cancellationToken = default)
        {
            ProcessedIds.Add(id);
            return new ValueTask<Result>(Result.Success());
        }

        public ValueTask<Result> MarkAsFailedAsync(Guid id,
            string reason,
            bool deadLetter,
            DateTimeOffset? nextAttemptAt = null,
            CancellationToken cancellationToken = default)
        {
            FailedIds.Add(id);
            return new ValueTask<Result>(Result.Success());
        }

        public ValueTask<Result> AddAsync<TEvent>(TEvent @event,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
        {
            return new ValueTask<Result>(Result.Success());
        }

        public ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }

        public ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IReadOnlyList<OutboxEntry>>(Array.Empty<OutboxEntry>());
        }

        public ValueTask<int?> GetAttemptCountAsync(Guid id,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<int?>(0);
        }

        public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(_entries.Length);
        }

        public ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(0);
        }

        public ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(0);
        }

        public ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }
    }

    /// <summary>
    ///     Records activities started on the Synapse source <b>by the test that created the probe</b>.
    /// </summary>
    /// <remarks>
    ///     An <see cref="ActivityListener" /> is process-global, and xunit runs test classes in parallel, so a
    ///     naive probe also captures the dispatch activities of any other class driving the outbox at the same
    ///     time — which made <c>Assert.Single</c> fail intermittently. The <see cref="AsyncLocal{T}" /> token is
    ///     set in the constructor, so it flows to this test's continuations and to nothing else; activities
    ///     started in another test's flow carry a different token and are ignored.
    /// </remarks>
    private sealed class ActivityProbe : IDisposable
    {
        private static readonly AsyncLocal<Guid> OwningTest = new();

        private readonly ActivityListener _listener;
        private readonly Guid _token = Guid.NewGuid();

        public ActivityProbe()
        {
            // Read the name before registering: AddActivityListener invokes ShouldListenTo for sources that
            // already exist, and touching SynapseActivitySource.Source from inside that callback would re-enter
            // its static initializer.
            var sourceName = SynapseActivitySource.Source.Name;

            OwningTest.Value = _token;
            Started = [];
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                {
                    if (OwningTest.Value == _token)
                    {
                        Started.Add(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Started { get; }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
