using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Contexts;

public sealed class ContextHandlerTests
{
    [Fact]
    public void IsInitialized_BeforeContextIsRead_IsFalse()
    {
        // Arrange (Given)
        using var handler = NewHandler(PropagatedContext.None, out _);

        // Act (When)
        var initializedBefore = handler.IsInitialized;
        _ = handler.Context;
        var initializedAfter = handler.IsInitialized;

        // Assert (Then) — asking must not be what creates the context
        Assert.False(initializedBefore);
        Assert.True(initializedAfter);
    }

    [Fact]
    public void Context_ReadTwice_ReturnsTheSameInstance()
    {
        // Arrange (Given)
        using var handler = NewHandler(PropagatedContext.None, out _);

        // Act (When)
        var first = handler.Context;
        var second = handler.Context;

        // Assert (Then)
        Assert.Same(first, second);
    }

    [Fact]
    public void Context_WhenInboundTraceIsPopulatedFirst_AdoptsTheTraceAndSpanIds()
    {
        // Arrange (Given)
        var trace = NewTraceContext();
        using var handler = NewHandler(new PropagatedContext(trace, null), out _);

        // Act (When)
        var context = handler.Context;

        // Assert (Then)
        Assert.Equal(trace.TraceId.ToHexString(), context.TraceId);
        Assert.Equal(trace.SpanId.ToHexString(), context.CausationId);
    }

    [Fact]
    public void Context_WhenStoreIsPopulatedAfterAnUnrelatedResolution_StillAdoptsIt()
    {
        // Arrange (Given) — the store is filled by a boundary adapter that may run after other services
        // have been constructed, as long as none of them has read the context yet
        using var handler = NewHandler(PropagatedContext.None, out var store);
        var trace = NewTraceContext();

        // Act (When)
        store.Inbound = store.Inbound with { Trace = trace };
        var context = handler.Context;

        // Assert (Then)
        Assert.Equal(trace.TraceId.ToHexString(), context.TraceId);
    }

    [Fact]
    public void Context_WithNoInboundState_MintsATraceId()
    {
        // Arrange (Given)
        using var handler = NewHandler(PropagatedContext.None, out _);

        // Act (When)
        var context = handler.Context;

        // Assert (Then) — 32 lowercase hex chars, and no dashes: this is a trace id, not a Guid
        Assert.Equal(32, context.TraceId.Length);
        Assert.DoesNotContain('-', context.TraceId);
        Assert.Matches("^[0-9a-f]{32}$", context.TraceId);
        Assert.Null(context.CausationId);
    }

    [Fact]
    public void Context_WithNoActivityListenerRegistered_StillHasATraceId()
    {
        // Arrange (Given) — the zero-OpenTelemetry host: StartActivity returns null and Activity.Current stays
        // null, so a computed TraceId would yield nothing and take the log scope with it
        Assert.Null(Activity.Current);
        using var handler = NewHandler(PropagatedContext.None, out _);

        // Act (When)
        var context = handler.Context;

        // Assert (Then)
        Assert.NotEmpty(context.TraceId);
    }

    [Fact]
    public void Context_WhenAnActivityIsCurrent_TraceIdEqualsTheAmbientActivity()
    {
        // Arrange (Given) — the guarantee that makes the stored value indistinguishable from reading
        // Activity.Current on the happy path. It holds because the value is taken from there.
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("ambient");
        Assert.NotNull(activity);

        using var handler = NewHandler(PropagatedContext.None, out _);

        // Act (When)
        var context = handler.Context;

        // Assert (Then)
        Assert.Equal(activity!.TraceId.ToHexString(), context.TraceId);
    }

    [Fact]
    public void Context_TraceId_IsStableAcrossReadsEvenWhenAnUnrelatedRootActivityStarts()
    {
        // Arrange (Given) — the failure mode a computed property would have: an unrelated root activity in a
        // different trace must not silently change this unit of work's identity
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);

        using var handler = NewHandler(PropagatedContext.None, out _);
        var first = handler.Context.TraceId;

        // Act (When)
        using (var unrelated = source.StartActivity("unrelated-root", ActivityKind.Internal,
                   default(ActivityContext)))
        {
            Assert.NotNull(unrelated);
            Assert.NotEqual(first, unrelated!.TraceId.ToHexString());
            var second = handler.Context.TraceId;

            // Assert (Then)
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void ResolvedIContext_AndAccessorContext_AreTheSameInstance()
    {
        // Arrange (Given) — IContext is registered as a scoped snapshot of the accessor's context. There must
        // be exactly one context object per scope, or the two views could report different identities.
        var services = new ServiceCollection()
            .AddLogging()
            .AddSynapse(cfg =>
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>())
            .BuildServiceProvider();

        using var scope = services.CreateScope();

        // Act (When)
        var injected = scope.ServiceProvider.GetRequiredService<IContext>();
        var viaAccessor = scope.ServiceProvider.GetRequiredService<IContextAccessor>()
            .Context;

        // Assert (Then)
        Assert.Same(injected, viaAccessor);
        Assert.Equal(injected.TraceId, viaAccessor.TraceId);
    }

    [Fact]
    public void ResolvedIContext_WhenInboundStoreIsPopulatedFirst_CarriesTheInboundTraceId()
    {
        // Arrange (Given) — mirrors what the ASP.NET Core middleware does before the endpoint runs
        var services = new ServiceCollection()
            .AddLogging()
            .AddSynapse(cfg =>
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>())
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        var trace = NewTraceContext();
        var store = scope.ServiceProvider.GetRequiredService<IInboundContextStore>();

        // Act (When)
        store.Inbound = store.Inbound with { Trace = trace };
        var injected = scope.ServiceProvider.GetRequiredService<IContext>();

        // Assert (Then)
        Assert.Equal(trace.TraceId.ToHexString(), injected.TraceId);
    }

    [Fact]
    public void Context_WhenRead_PublishesTheContextToTheAmbientSlot()
    {
        // Arrange (Given) — the ambient mirror is how SynapsePropagationHandler finds the context from the
        // scope IHttpClientFactory built it in (known issue 033)
        using var handler = NewHandler(PropagatedContext.None, out _);
        Assert.Null(AmbientContext.Value);

        // Act (When)
        var context = handler.Context;

        // Assert (Then)
        Assert.Same(context, AmbientContext.Value);
    }

    [Fact]
    public void Dispose_AfterAContextWasPublished_RestoresThePreviousAmbientContext()
    {
        // Arrange (Given) — scopes nest: a handler can dispatch through a child scope, and the outer unit of
        // work must not keep seeing the inner context once the inner scope is gone
        using var outer = NewHandler(PropagatedContext.None, out _);
        var outerContext = outer.Context;

        // Act (When)
        var inner = NewHandler(PropagatedContext.None, out _);
        var innerContext = inner.Context;
        var whileNested = AmbientContext.Value;
        inner.Dispose();

        // Assert (Then)
        Assert.Same(innerContext, whileNested);
        Assert.Same(outerContext, AmbientContext.Value);
    }

    [Fact]
    public void Dispose_WhenTheContextWasNeverRead_LeavesTheAmbientSlotAlone()
    {
        // Arrange (Given) — a scope that never touched the mediator publishes nothing, so it must restore
        // nothing either: clearing on the way out would erase an enclosing scope's context
        using var outer = NewHandler(PropagatedContext.None, out _);
        var outerContext = outer.Context;
        var untouched = NewHandler(PropagatedContext.None, out _);

        // Act (When)
        untouched.Dispose();

        // Assert (Then)
        Assert.Same(outerContext, AmbientContext.Value);
    }

    [Fact]
    public void Context_ReadConcurrently_ReturnsOneInstanceWithOneTraceId()
    {
        // Arrange (Given) — concurrent event handlers share one scope, so two of them can reach the accessor at
        // once. Without synchronization each found the field empty, each built a context with its own minted trace
        // id, and each used the one it built while only one won the field: identity divergence (known issue 036).
        const int readers = 32;
        using var handler = NewHandler(PropagatedContext.None, out _);
        var contexts = new IContext[readers];

        // Act (When)
        Race.Run(readers, i => contexts[i] = handler.Context);

        // Assert (Then)
        Assert.Single(contexts.Distinct());
        Assert.Single(contexts.Select(c => c.TraceId)
                              .Distinct());
    }

    [Fact]
    public async Task Context_ReadFromASiblingBranch_IsAlsoPublishedToThatBranchesAmbientSlot()
    {
        // Arrange (Given) — an AsyncLocal write does not cross into a sibling branch, so the handler that did not
        // create the context would otherwise find the ambient slot empty and its outbound calls would stamp
        // nothing (the same symptom as known issue 033, one branch over)
        using var handler = NewHandler(PropagatedContext.None, out _);
        var context = handler.Context;

        // Act (When)
        var (ambientInSibling, contextInSibling) = await Task.Run(() =>
        {
            AmbientContext.Exchange(null);
            var read = handler.Context;
            return (AmbientContext.Value, read);
        });

        // Assert (Then)
        Assert.Same(context, contextInSibling);
        Assert.Same(context, ambientInSibling);
    }

    [Fact]
    public void Context_WhenAmbientTraceIsSuppressed_MintsInsteadOfAdoptingTheAmbientActivity()
    {
        // Arrange (Given) — the untrusted edge configuration: the host's instrumentation has already parented
        // the request activity to the caller's traceparent, so clearing the inbound trace is not enough on its
        // own to refuse it (known issue 032)
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("request");
        Assert.NotNull(activity);

        using var handler = NewHandler(new PropagatedContext(default, null, SuppressAmbientTrace: true), out _);

        // Act (When)
        var context = handler.Context;

        // Assert (Then)
        Assert.Matches("^[0-9a-f]{32}$", context.TraceId);
        Assert.NotEqual(activity!.TraceId.ToHexString(), context.TraceId);
        Assert.Null(context.CausationId);
    }

    [Fact]
    public void Context_WhenAmbientTraceIsSuppressedButTraceWasAdoptedAnyway_PrefersTheInboundTrace()
    {
        // Arrange (Given) — suppression only disqualifies the ambient activity. A boundary that both trusted the
        // caller and set the flag would be contradicting itself; the explicit trace still wins.
        var trace = NewTraceContext();
        using var handler = NewHandler(new PropagatedContext(trace, null, SuppressAmbientTrace: true), out _);

        // Act (When)
        var context = handler.Context;

        // Assert (Then)
        Assert.Equal(trace.TraceId.ToHexString(), context.TraceId);
    }

    private static ActivityContext NewTraceContext()
    {
        return new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
    }

    private static ActivityListener NewRecordingListener(out string sourceName)
    {
        sourceName = $"synapse-context-handler-test-{Guid.NewGuid():N}";
        var name = sourceName;

        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static ContextHandler NewHandler(PropagatedContext inbound,
        out IInboundContextStore store)
    {
        store = new InboundContextStore { Inbound = inbound };
        return new ContextHandler(new DefaultContextFactory(), store);
    }
}
