using System.Diagnostics;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Contexts;

public sealed class ContextTests
{
    [Fact]
    public void Context_FeatureApis_WorkAsExpected()
    {
        // Arrange (Given)
        var context = NewContext();
        var feature = new TestFeature("v1");

        // Act (When)
        context.SetFeature(feature);
        var hasFeature = context.TryGetFeature<TestFeature>(out var tryFeature);
        var getFeature = context.GetFeature<TestFeature>();
        var mustFeature = context.MustGetFeature<TestFeature>();
        context.RemoveFeature<TestFeature>();

        // Assert (Then)
        Assert.True(hasFeature);
        Assert.Same(feature, tryFeature);
        Assert.Same(feature, getFeature);
        Assert.Same(feature, mustFeature);
        Assert.Null(context.GetFeature<TestFeature>());
        Assert.False(context.TryGetFeature<TestFeature>(out _));
    }

    [Fact]
    public void Context_Identity_ExposesSuppliedValues()
    {
        // Arrange (Given)
        var identity = new ContextIdentity(
            ActivityTraceId.CreateRandom().ToHexString(),
            ActivitySpanId.CreateRandom().ToHexString(),
            DateTimeOffset.UnixEpoch);

        // Act (When)
        var context = new Context(identity);

        // Assert (Then)
        Assert.Equal(identity.TraceId, context.TraceId);
        Assert.Equal(identity.CausationId, context.CausationId);
        Assert.Equal(identity.OccurredAt, context.OccurredAt);
    }

    [Fact]
    public void Context_BaggageApis_WorkAsExpected()
    {
        // Arrange (Given)
        var context = NewContext();

        // Act (When)
        var stored = context.SetBaggage("tenant.id", "contoso");
        var gotByTryGet = context.TryGetBaggage("tenant.id", out var tryValue);
        var gotByGet = context.GetBaggage("tenant.id");
        var removed = context.RemoveBaggage("tenant.id");
        var removedAgain = context.RemoveBaggage("tenant.id");

        // Assert (Then)
        Assert.True(stored);
        Assert.True(gotByTryGet);
        Assert.Equal("contoso", tryValue);
        Assert.Equal("contoso", gotByGet);
        Assert.True(removed);
        Assert.False(removedAgain);
        Assert.Empty(context.Baggage);
    }

    [Fact]
    public void Context_GetBaggage_WhenKeyMissing_ReturnsNull()
    {
        // Arrange (Given)
        var context = NewContext();

        // Act (When)
        var value = context.GetBaggage("nonexistent");

        // Assert (Then)
        Assert.Null(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has,comma")]
    [InlineData("has=equals")]
    [InlineData("has\nnewline")]
    public void Context_SetBaggage_WhenKeyIsNotSerializable_ReturnsFalse(string key)
    {
        // Arrange (Given)
        var context = NewContext();

        // Act (When)
        var stored = context.SetBaggage(key, "value");

        // Assert (Then)
        Assert.False(stored);
        Assert.Empty(context.Baggage);
    }

    [Theory]
    [InlineData("has\nnewline")]
    [InlineData("has\ttab")]
    public void Context_SetBaggage_WhenValueIsNotSerializable_ReturnsFalse(string value)
    {
        // Arrange (Given)
        var context = NewContext();

        // Act (When)
        var stored = context.SetBaggage("key", value);

        // Assert (Then)
        Assert.False(stored);
        Assert.Empty(context.Baggage);
    }

    [Theory]
    [InlineData("Acme, Inc")]
    [InlineData("a=b")]
    [InlineData("100% coffee")]
    public void Context_SetBaggage_WhenValueContainsADelimiter_AcceptsIt(string value)
    {
        // Arrange (Given) — values are percent-encoded on the wire, so a delimiter inside one is escaped rather
        // than ambiguous. Refusing them rejected values the W3C specification allows (known issue 038).
        var context = NewContext();

        // Act (When)
        var stored = context.SetBaggage("company.name", value);

        // Assert (Then)
        Assert.True(stored);
        Assert.Equal(value, context.GetBaggage("company.name"));
    }

    [Fact]
    public void Context_SetBaggage_WhenEntryCountLimitReached_RejectsNewKeys()
    {
        // Arrange (Given) — fill baggage to exactly the W3C entry limit with small entries
        var context = NewContext();
        for (var i = 0; i < BaggageLimits.MaxEntryCount; i++)
        {
            Assert.True(context.SetBaggage($"k{i}", "v"));
        }

        // Act (When)
        var extra = context.SetBaggage("one-too-many", "v");
        var replacement = context.SetBaggage("k0", "replaced");

        // Assert (Then)
        Assert.False(extra);
        Assert.True(replacement); // replacing an existing key does not add an entry
        Assert.Equal(BaggageLimits.MaxEntryCount, context.Baggage.Count);
    }

    [Fact]
    public void Context_SetBaggage_WhenTotalByteLimitExceeded_ReturnsFalse()
    {
        // Arrange (Given) — 1000-byte values exhaust the 8192-byte budget well before the entry-count limit
        var context = NewContext();
        var chunk = new string('x', 1000);
        var accepted = 0;
        while (context.SetBaggage($"k{accepted}", chunk))
        {
            accepted++;
        }

        // Act (When) — another value of the same size cannot fit in the remaining budget either
        var repeated = context.SetBaggage("another", chunk);

        // Assert (Then)
        Assert.InRange(accepted, 1, BaggageLimits.MaxEntryCount - 1);
        Assert.False(repeated);
        Assert.Equal(accepted, context.Baggage.Count);
        Assert.InRange(TotalBaggageBytes(context), 1, BaggageLimits.MaxTotalBytes);
    }

    [Fact]
    public void Context_SetBaggage_WhenBudgetPartlyRemains_StillAcceptsASmallEntry()
    {
        // Arrange (Given) — the cap is on total bytes, so a rejection of a large value must not close
        // baggage entirely while room remains
        var context = NewContext();
        var chunk = new string('x', 1000);
        while (context.SetBaggage($"k{context.Baggage.Count}", chunk))
        {
        }

        // Act (When)
        var tinyEntry = context.SetBaggage("tiny", "v");

        // Assert (Then)
        Assert.True(tinyEntry);
        Assert.InRange(TotalBaggageBytes(context), 1, BaggageLimits.MaxTotalBytes);
    }

    [Fact]
    public void Context_SetBaggage_WhenReplacingSameKey_DoesNotAccumulateBytes()
    {
        // Arrange (Given)
        var context = NewContext();
        var chunk = new string('x', 4000);

        // Act (When) — overwriting the same key repeatedly must not exhaust the byte budget
        var results = new List<bool>();
        for (var i = 0; i < 10; i++)
        {
            results.Add(context.SetBaggage("key", chunk));
        }

        // Assert (Then)
        Assert.All(results, Assert.True);
        Assert.Single(context.Baggage);
    }

    [Fact]
    public void Context_MustGetFeature_WhenMissing_Throws()
    {
        // Arrange (Given)
        var context = NewContext();

        // Act (When)
        var action = () => context.MustGetFeature<TestFeature>();

        // Assert (Then)
        var exception = Assert.Throws<MissingContextFeatureException>(action);
        Assert.Contains(nameof(TestFeature), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_DoesNotSnapshotTracingState_WhenActivityIsPresent()
    {
        // Arrange (Given) — trace state stays ambient on Activity.Current; snapshotting it into the
        // context would go stale as pipeline stages start child activities.
        using var activity = new Activity("synapse-test");
        activity.AddBaggage("tenant", "contoso");
        activity.Start();

        // Act (When)
        var context = NewContext();

        // Assert (Then)
        Assert.Empty(context.Baggage);
    }

    [Fact]
    public void DefaultContextFactory_Create_WithNothingPropagated_MintsARootTraceId()
    {
        // Arrange (Given)
        var factory = new DefaultContextFactory();
        var before = DateTimeOffset.UtcNow;

        // Act (When)
        var context = factory.Create(PropagatedContext.None);

        // Assert (Then)
        Assert.Matches("^[0-9a-f]{32}$", context.TraceId);
        Assert.Null(context.CausationId); // nothing was propagated, so this is the root of a flow
        Assert.InRange(context.OccurredAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ContextIdentity_ForUnitOfWork_WithZeroTraceIdOnAmbientActivity_MintsATraceId()
    {
        // Arrange (Given) — an unstarted Activity has a default trace id, which hex-formats to 32 zeros.
        // Known issue 031: that string is non-empty, so it used to be adopted as the identity.
        var previous = Activity.Current;
        Activity.Current = new Activity("unstarted");

        try
        {
            // Act (When)
            var identity = ContextIdentity.ForUnitOfWork(PropagatedContext.None);

            // Assert (Then)
            Assert.Matches("^[0-9a-f]{32}$", identity.TraceId);
            Assert.NotEqual(default(ActivityTraceId).ToHexString(), identity.TraceId);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void DefaultContextFactory_Create_WithInboundTrace_AdoptsItAtCreation()
    {
        // Arrange (Given)
        var factory = new DefaultContextFactory();
        var trace = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
        var inbound = new PropagatedContext(trace,
            new Dictionary<string, string> { ["tenant.id"] = "contoso" });

        // Act (When)
        var context = factory.Create(inbound);

        // Assert (Then) — the sender's span id is what caused this unit of work
        Assert.Equal(trace.TraceId.ToHexString(), context.TraceId);
        Assert.Equal(trace.SpanId.ToHexString(), context.CausationId);
        Assert.Equal("contoso", context.GetBaggage("tenant.id"));
    }

    [Fact]
    public void SlimContextFactory_Create_KeepsIdentityButDiscardsBaggage()
    {
        // Arrange (Given) — the slim factory's only difference: baggage is not restored, which is why it has
        // to be selected explicitly
        var factory = new SlimContextFactory();
        var trace = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
        var inbound = new PropagatedContext(trace,
            new Dictionary<string, string> { ["tenant.id"] = "contoso" });

        // Act (When)
        var context = factory.Create(inbound);

        // Assert (Then)
        Assert.Equal(trace.TraceId.ToHexString(), context.TraceId);
        Assert.Equal(trace.SpanId.ToHexString(), context.CausationId);
        Assert.Empty(context.Baggage);
    }

    [Fact]
    public void DefaultContextFactory_Create_WithOversizedInboundBaggage_DropsExcessInsteadOfFailing()
    {
        // Arrange (Given) — a peer sending baggage beyond the W3C cap must degrade, not break the request
        var factory = new DefaultContextFactory();
        var chunk = new string('x', 1000);
        var oversized = new Dictionary<string, string>();
        for (var i = 0; i < 20; i++)
        {
            oversized[$"k{i}"] = chunk;
        }

        // Act (When)
        var context = factory.Create(new PropagatedContext(default, oversized));

        // Assert (Then)
        Assert.NotEmpty(context.Baggage);
        Assert.True(context.Baggage.Count < oversized.Count);
        Assert.InRange(TotalBaggageBytes(context), 1, BaggageLimits.MaxTotalBytes);
    }

    [Fact]
    public void DefaultContextFactory_Create_WithInboundBaggage_LeavesTheByteBudgetExact()
    {
        // Arrange (Given) — inbound baggage is applied as one batch write rather than entry by entry, so the batch
        // has to keep the byte counter as exact as the single-entry path does: understating it would let an
        // oversized entry through, overstating it would reject one that fits
        var factory = new DefaultContextFactory();
        var inbound = new Dictionary<string, string>();
        for (var i = 0; i < 8; i++)
        {
            inbound[$"k{i}"] = new string('x', 100);
        }

        // Act (When)
        var context = factory.Create(new PropagatedContext(default, inbound));

        // Assert (Then) — exactly the remaining budget fits, and one byte more does not
        var remaining = BaggageLimits.MaxTotalBytes - TotalBaggageBytes(context);
        Assert.Equal(8, context.Baggage.Count);
        Assert.False(context.SetBaggage("probe", new string('x', remaining - 6)));
        Assert.True(context.SetBaggage("probe", new string('x', remaining - 7)));
    }

    [Fact]
    public void DefaultContextFactory_Create_WithMoreInboundEntriesThanTheCap_KeepsTheCap()
    {
        // Arrange (Given) — the entry-count cap has to hold within a single batch, not just across calls
        var factory = new DefaultContextFactory();
        var inbound = new Dictionary<string, string>();
        for (var i = 0; i < BaggageLimits.MaxEntryCount + 10; i++)
        {
            inbound[$"k{i}"] = "v";
        }

        // Act (When)
        var context = factory.Create(new PropagatedContext(default, inbound));

        // Assert (Then)
        Assert.Equal(BaggageLimits.MaxEntryCount, context.Baggage.Count);
        Assert.False(context.SetBaggage("one-too-many", "v"));
    }

    [Fact]
    public void SlimContextFactory_Create_WithNothingPropagated_MintsARootTraceId()
    {
        // Arrange (Given) — dropping baggage must not cost identity: the slim factory still has to mint a trace
        // id when there is nothing inbound to adopt
        var factory = new SlimContextFactory();

        // Act (When)
        var context = factory.Create(PropagatedContext.None);

        // Assert (Then)
        Assert.Matches("^[0-9a-f]{32}$", context.TraceId);
        Assert.Null(context.CausationId);
        Assert.Empty(context.Baggage);
    }

    [Fact]
    public void SetBaggage_FromConcurrentHandlers_KeepsEveryEntryAndItsByteCount()
    {
        // Arrange (Given) — ConcurrentEventOrchestrator runs every handler for an event with Task.WhenAll against
        // the one context of the scope, so concurrent SetBaggage is an ordinary occurrence (known issue 035). A
        // plain Dictionary corrupts under it, and the byte counter desyncs from the content it guards.
        var context = NewContext();
        const int writers = 32;

        // Act (When)
        Race.Run(writers, i => Assert.True(context.SetBaggage($"tenant.{i}", $"value-{i}")));

        // Assert (Then) — no entry lost, and the counter still describes exactly what is stored
        Assert.Equal(writers, context.Baggage.Count);
        for (var i = 0; i < writers; i++)
        {
            Assert.Equal($"value-{i}", context.GetBaggage($"tenant.{i}"));
        }

        Assert.True(context.SetBaggage("probe", new string('x', 8192 - TotalBaggageBytes(context) - 8)));
    }

    [Fact]
    public void SetBaggage_AndRemoveBaggage_UnderConcurrentChurn_KeepTheByteCounterExact()
    {
        // Arrange (Given) — the byte counter is a read-modify-write across the whole collection, so racing
        // writers lose updates and it drifts away from the content it is supposed to describe. Drift is invisible
        // until it silently starts rejecting entries that would have fitted.
        var context = NewContext();
        const int writers = 16;
        const int iterations = 200;

        // Act (When) — every worker leaves exactly what it found, so the counter must end at zero
        Race.Run(writers, i =>
        {
            for (var n = 0; n < iterations; n++)
            {
                Assert.True(context.SetBaggage($"tenant.{i}", $"value-{i}-{n}"));
                Assert.True(context.RemoveBaggage($"tenant.{i}"));
            }
        });

        // Assert (Then) — empty baggage must accept an entry that fills the whole budget
        Assert.Empty(context.Baggage);
        Assert.True(context.SetBaggage("probe", new string('x', BaggageLimits.MaxTotalBytes - 8)));
    }

    [Fact]
    public void Baggage_EnumeratedWhileAnotherHandlerWrites_DoesNotThrow()
    {
        // Arrange (Given) — W3CContextPropagator.Inject and LoggingEnrichmentBehavior both enumerate
        // context.Baggage; a sibling handler adding or removing an entry at that moment used to throw
        // "Collection was modified; enumeration operation may not execute"
        var context = NewContext();
        context.SetBaggage("tenant.id", "contoso");

        // Act (When) — one writer churning the collection's shape against many readers walking it
        Race.Run(4, worker =>
        {
            if (worker == 0)
            {
                for (var i = 0; i < 20_000; i++)
                {
                    context.SetBaggage($"key.{i % 8}", "value");
                    context.RemoveBaggage($"key.{i % 8}");
                }

                return;
            }

            for (var i = 0; i < 20_000; i++)
            {
                foreach (var entry in context.Baggage)
                {
                    Assert.NotNull(entry.Value);
                }
            }
        });

        // Assert (Then) — Race.Run rethrows anything a worker threw
        Assert.Equal("contoso", context.GetBaggage("tenant.id"));
    }

    [Fact]
    public void SetFeature_FromConcurrentHandlers_LandsInOneStore()
    {
        // Arrange (Given) — features are created on first write, so racing writers must not each get a store of
        // their own; the CQRS boundary marker is set and read this way
        var context = NewContext();
        const int writers = 16;

        // Act (When)
        Race.Run(writers, i =>
        {
            context.SetFeature(new TestFeature($"v{i}"));
            Assert.NotNull(context.GetFeature<TestFeature>());
        });

        // Assert (Then) — one keyed slot, last writer wins, and every racer saw a feature rather than null
        Assert.NotNull(context.GetFeature<TestFeature>());
        context.RemoveFeature<TestFeature>();
        Assert.Null(context.GetFeature<TestFeature>());
    }

    private static int TotalBaggageBytes(IContext context)
    {
        return context.Baggage.Sum(entry => BaggageLimits.MeasureEntry(entry.Key, entry.Value));
    }

    private static Context NewContext()
    {
        var identity = new ContextIdentity(ActivityTraceId.CreateRandom().ToHexString(), null,
            DateTimeOffset.UtcNow);
        return new Context(identity);
    }

    private sealed record TestFeature(string Value) : IContextFeature
    {
        public string Name => "TestFeature";
    }
}
