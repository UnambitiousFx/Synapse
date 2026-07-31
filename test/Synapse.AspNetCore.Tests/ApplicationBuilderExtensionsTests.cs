using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore.Tests;

public sealed class ApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task UseSynapsePropagation_WithInboundTraceContext_RecordsItAsInboundState()
    {
        // Arrange (Given)
        var trace = NewTraceContext();
        var store = new TestInboundContextStore();
        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(new PropagatedContext(trace, null)));

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then)
        Assert.Equal(trace.TraceId, store.Inbound.Trace.TraceId);
        Assert.Equal(trace.SpanId, store.Inbound.Trace.SpanId);
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenTrusting_KeepsPropagatedBaggage()
    {
        // Arrange (Given)
        var propagated = new PropagatedContext(NewTraceContext(),
            new Dictionary<string, string> { ["tenant.id"] = "contoso" });
        var store = new TestInboundContextStore();
        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(propagated));

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then)
        Assert.Equal("contoso", store.Inbound.Baggage!["tenant.id"]);
    }

    [Fact]
    public async Task UseSynapsePropagation_WithNoInboundTraceContext_LeavesInboundStateEmpty()
    {
        // Arrange (Given) — a request with no traceparent simply starts a new flow
        var store = new TestInboundContextStore();
        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(PropagatedContext.None));

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then)
        Assert.True(store.Inbound.IsEmpty);
    }

    [Fact]
    public async Task UseSynapsePropagation_IgnoresAnInboundTraceIdHeader()
    {
        // Arrange (Given) — the Trace-Id header is response-only. Synapse reads no identity header of its own;
        // identity comes from traceparent, so a client sending Trace-Id on the request is not believed.
        var store = new TestInboundContextStore();
        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(PropagatedContext.None));
        httpContext.Request.Headers.Append(PropagationOptions.DefaultTraceIdHeaderName,
            Guid.NewGuid().ToString());

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then)
        Assert.True(store.Inbound.IsEmpty);
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenNoInboundStoreIsRegistered_DoesNotThrow()
    {
        // Arrange (Given) — the middleware may be added to an app that never called AddSynapse
        var httpContext = BuildHttpContext(out var pipeline);
        httpContext.Request.Headers.Append("traceparent",
            "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

        // Act (When)
        var act = async () => await pipeline(httpContext);

        // Assert (Then)
        await act();
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenNotTrusting_DemotesCallerTraceIdToBaggageAndDropsTheRest()
    {
        // Arrange (Given) — the edge configuration: nothing caller-controlled becomes this flow's identity
        var trace = NewTraceContext();
        var propagated = new PropagatedContext(trace,
            new Dictionary<string, string> { ["tenant.id"] = "spoofed" });
        var store = new TestInboundContextStore();

        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(propagated),
            configure: o => o.TrustIncomingHeader = false);

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then) — clearing the trace is not enough: the host's instrumentation has already parented the
        // request activity to the caller, so the ambient activity must be disqualified too (known issue 032)
        Assert.Equal(default, store.Inbound.Trace);
        Assert.True(store.Inbound.SuppressAmbientTrace);
        Assert.Equal(trace.TraceId.ToHexString(), store.Inbound.Baggage!["client.trace_id"]);
        Assert.DoesNotContain("tenant.id", store.Inbound.Baggage.Keys);
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenNotTrustingAndTheHostAdoptedTheCallersTrace_MintsTheTraceId()
    {
        // Arrange (Given) — the full untrusted path with request tracing enabled, which is the deployment that
        // cares: the ambient activity carries the caller's trace id, and the resulting identity must not.
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        var callerTrace = NewTraceContext();
        using var activity = source.StartActivity("request", ActivityKind.Server, callerTrace);
        Assert.NotNull(activity);
        Assert.Equal(callerTrace.TraceId, activity!.TraceId);

        var store = new TestInboundContextStore();
        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(new PropagatedContext(callerTrace, null)),
            configure: o => o.TrustIncomingHeader = false);

        // Act (When)
        await pipeline(httpContext);
        var identity = ContextIdentity.ForUnitOfWork(store.Inbound);

        // Assert (Then)
        Assert.NotEqual(callerTrace.TraceId.ToHexString(), identity.TraceId);
        Assert.Matches("^[0-9a-f]{32}$", identity.TraceId);
        Assert.Null(identity.CausationId);
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenTrusting_LeavesTheAmbientActivityUsable()
    {
        // Arrange (Given) — the default: continuing the caller's trace is the point of the middleware, so
        // nothing is suppressed and a request with no traceparent may still take its id from the host's activity
        var store = new TestInboundContextStore();
        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(PropagatedContext.None));

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then)
        Assert.False(store.Inbound.SuppressAmbientTrace);
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenNotTrustingAndNoCallerTrace_LeavesBaggageEmpty()
    {
        // Arrange (Given)
        var store = new TestInboundContextStore();
        var httpContext = BuildHttpContext(out var pipeline, store: store,
            propagator: new StubPropagator(PropagatedContext.None),
            configure: o => o.TrustIncomingHeader = false);

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then) — no state was recovered, but the ambient activity stays disqualified: a caller whose
        // traceparent the host adopted before this middleware ran must not supply identity through it either
        Assert.Null(store.Inbound.Baggage);
        Assert.True(store.Inbound.IsEmpty);
        Assert.True(store.Inbound.SuppressAmbientTrace);
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenContextWasCreated_WritesTheTraceIdOnStarting()
    {
        // Arrange (Given)
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var context = Substitute.For<IContext>();
        context.TraceId.Returns(traceId);
        var accessor = new TestContextAccessor { Context = context, IsInitialized = true };

        var httpContext = BuildHttpContext(out var pipeline, accessor);
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then) — plain 32-hex, so the same string can be pasted into a tracing backend
        Assert.True(httpContext.Response.Headers
            .TryGetValue(PropagationOptions.DefaultTraceIdHeaderName, out var header));
        Assert.Equal(traceId, header.ToString());
        Assert.DoesNotContain('-', header.ToString());
    }

    [Fact]
    public void DefaultTraceIdHeaderName_NamesTheSameConceptAsTheContextProperty()
    {
        // Arrange (Given) / Act (When) / Assert (Then)
        // The header carries IContext.TraceId, so it is named after it — one value must not answer to two names.
        // And no "X-" prefix: RFC 6648 deprecated that convention in 2012.
        Assert.Equal("Trace-Id", PropagationOptions.DefaultTraceIdHeaderName);
        Assert.DoesNotContain("X-", PropagationOptions.DefaultTraceIdHeaderName,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Correlation", PropagationOptions.DefaultTraceIdHeaderName,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenAnActivityIsCurrent_WritesTraceResponseAlongsideTheBareTraceId()
    {
        // Arrange (Given)
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("request");
        Assert.NotNull(activity);

        var context = Substitute.For<IContext>();
        context.TraceId.Returns(activity!.TraceId.ToHexString());
        var accessor = new TestContextAccessor { Context = context, IsInitialized = true };

        var httpContext = BuildHttpContext(out var pipeline, accessor);
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then) — the bare id for people, the full W3C form for tooling
        Assert.Equal(activity.TraceId.ToHexString(),
            httpContext.Response.Headers[PropagationOptions.DefaultTraceIdHeaderName].ToString());
        Assert.Equal($"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01",
            httpContext.Response.Headers[PropagationKeys.TraceResponse].ToString());
    }

    [Fact]
    public async Task UseSynapsePropagation_TraceResponseCarriesTheContextsTraceIdNotTheAmbientOne()
    {
        // Arrange (Given) — in untrusted mode the host's activity may hold the caller's inbound trace while the
        // context holds a server-minted one. The two response headers must not contradict each other.
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("request");
        Assert.NotNull(activity);

        var mintedTraceId = ActivityTraceId.CreateRandom().ToHexString();
        Assert.NotEqual(activity!.TraceId.ToHexString(), mintedTraceId);

        var context = Substitute.For<IContext>();
        context.TraceId.Returns(mintedTraceId);
        var accessor = new TestContextAccessor { Context = context, IsInitialized = true };

        var httpContext = BuildHttpContext(out var pipeline, accessor);
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then)
        var traceResponse = httpContext.Response.Headers[PropagationKeys.TraceResponse].ToString();
        Assert.StartsWith($"00-{mintedTraceId}-", traceResponse, StringComparison.Ordinal);
        Assert.DoesNotContain(activity.TraceId.ToHexString(), traceResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseSynapsePropagation_WithNoActivity_WritesTheBareTraceIdButNoTraceResponse()
    {
        // Arrange (Given) — traceresponse describes the response's span; with no span there is nothing to report
        Assert.Null(Activity.Current);
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var context = Substitute.For<IContext>();
        context.TraceId.Returns(traceId);
        var accessor = new TestContextAccessor { Context = context, IsInitialized = true };

        var httpContext = BuildHttpContext(out var pipeline, accessor);
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then)
        Assert.Equal(traceId,
            httpContext.Response.Headers[PropagationOptions.DefaultTraceIdHeaderName].ToString());
        Assert.False(httpContext.Response.Headers.ContainsKey(PropagationKeys.TraceResponse));
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenTraceResponseIsDisabled_WritesOnlyTheBareTraceId()
    {
        // Arrange (Given)
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("request");
        Assert.NotNull(activity);

        var context = Substitute.For<IContext>();
        context.TraceId.Returns(activity!.TraceId.ToHexString());
        var accessor = new TestContextAccessor { Context = context, IsInitialized = true };

        var httpContext = BuildHttpContext(out var pipeline, accessor,
            configure: o => o.EmitTraceResponse = false);
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then)
        Assert.True(httpContext.Response.Headers
            .ContainsKey(PropagationOptions.DefaultTraceIdHeaderName));
        Assert.False(httpContext.Response.Headers.ContainsKey(PropagationKeys.TraceResponse));
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenNoContextWasCreated_WritesNeitherResponseHeader()
    {
        // Arrange (Given)
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("request");

        var accessor = new TestContextAccessor { IsInitialized = false };
        var httpContext = BuildHttpContext(out var pipeline, accessor);
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then)
        Assert.False(httpContext.Response.Headers
            .ContainsKey(PropagationOptions.DefaultTraceIdHeaderName));
        Assert.False(httpContext.Response.Headers.ContainsKey(PropagationKeys.TraceResponse));
    }

    [Fact]
    public async Task UseSynapsePropagation_WithCustomHeaderName_WritesThatResponseHeader()
    {
        // Arrange (Given)
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var context = Substitute.For<IContext>();
        context.TraceId.Returns(traceId);
        var accessor = new TestContextAccessor { Context = context, IsInitialized = true };

        var httpContext = BuildHttpContext(out var pipeline, accessor,
            configure: o => o.TraceIdHeaderName = "X-Request-Trace");
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then)
        Assert.Equal(traceId, httpContext.Response.Headers["X-Request-Trace"].ToString());
        Assert.False(httpContext.Response.Headers
            .ContainsKey(PropagationOptions.DefaultTraceIdHeaderName));
    }

    [Fact]
    public async Task UseSynapsePropagation_WhenNoContextWasCreated_WritesNoResponseHeader()
    {
        // Arrange (Given) — a route that never touches the mediator must not get an id invented for it
        var accessor = new TestContextAccessor { IsInitialized = false };
        var httpContext = BuildHttpContext(out var pipeline, accessor);
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then)
        Assert.False(httpContext.Response.Headers
            .ContainsKey(PropagationOptions.DefaultTraceIdHeaderName));
        Assert.False(accessor.ContextWasRead);
    }

    private static ActivityContext NewTraceContext()
    {
        return new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
    }

    private static ActivityListener NewRecordingListener(out string sourceName)
    {
        sourceName = $"synapse-middleware-test-{Guid.NewGuid():N}";
        var name = sourceName;

        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static DefaultHttpContext BuildHttpContext(out RequestDelegate pipeline,
        IContextAccessor? accessor = null,
        IInboundContextStore? store = null,
        IContextPropagator? propagator = null,
        Action<PropagationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        if (accessor is not null)
        {
            services.AddSingleton(accessor);
        }

        if (store is not null)
        {
            services.AddSingleton(store);
        }

        if (propagator is not null)
        {
            services.AddSingleton(propagator);
        }

        var serviceProvider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(serviceProvider);
        app.UseSynapsePropagation(configure);
        app.Run(ctx => ctx.Response.WriteAsync("ok"));
        pipeline = app.Build();

        return new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
    }

    private sealed class TestInboundContextStore : IInboundContextStore
    {
        public PropagatedContext Inbound { get; set; } = PropagatedContext.None;
    }

    private sealed class StubPropagator : IContextPropagator
    {
        private readonly PropagatedContext _extracted;

        public StubPropagator(PropagatedContext extracted)
        {
            _extracted = extracted;
        }

        public void Inject(IContext context,
            IPropagationCarrier carrier)
        {
        }

        public PropagatedContext Extract(IPropagationCarrier carrier)
        {
            return _extracted;
        }
    }

    private sealed class TestContextAccessor : IContextAccessor
    {
        private readonly IContext _context = Substitute.For<IContext>();

        public bool ContextWasRead { get; private set; }

        public required bool IsInitialized { get; init; }

        public IContext Context
        {
            get
            {
                ContextWasRead = true;
                return field ?? _context;
            }
            init;
        }
    }

    private sealed class TestResponseFeature : IHttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> _onStartingCallbacks = new();

        public int StatusCode { get; set; }
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _onStartingCallbacks.Push((callback, state));
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task InvokeOnStartingAsync()
        {
            while (_onStartingCallbacks.Count > 0)
            {
                var callback = _onStartingCallbacks.Pop();
                await callback.Callback(callback.State);
            }

            HasStarted = true;
        }
    }
}
