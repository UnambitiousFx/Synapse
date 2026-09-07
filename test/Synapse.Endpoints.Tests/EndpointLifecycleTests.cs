using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class EndpointLifecycleTests
{
    private static readonly List<string> Order = [];

    [Fact]
    public async Task HandleAsync_WithEveryHookAndProcessor_RunsInTheDocumentedOrder()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<TracingEndpoint>(new EndpointMetadata(["GET"], "/trace"));

        var context = ContextWith(services =>
        {
            services.AddScoped<TracingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, Ok());

        var descriptor = ((EndpointBase)new TracingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<TracingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(
            ["pre-processor", "bind", "before-hook", "dispatch", "after-hook:200", "post-processor"],
            Order);
    }

    [Fact]
    public async Task OnBeforeHandleAsync_ReturningAResult_SkipsDispatchButStillRunsTheExitSteps()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<ShortCircuitingEndpoint>(
            new EndpointMetadata(["GET"], "/short-circuit"));

        var invoker = Substitute.For<IHttpInvoker>();
        var context = ContextWith(services => services.AddScoped<TracingPostProcessor>(), invoker);

        var descriptor = ((EndpointBase)new ShortCircuitingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<ShortCircuitingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.DoesNotContain("dispatch", Order);
        Assert.Contains("post-processor", Order);
        await invoker.DidNotReceiveWithAnyArgs()
            .InvokeAsync(Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreProcessor_ReturningAResult_SkipsBinding()
    {
        // Arrange: pre-processors run before binding, so a rejection does not pay to bind.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<RejectingEndpoint>(new EndpointMetadata(["GET"], "/reject"));

        var context = ContextWith(services => services.AddScoped<RejectingPreProcessor>(), Ok());

        var descriptor = ((EndpointBase)new RejectingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<RejectingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.DoesNotContain("bind", Order);
    }

    [Fact]
    public async Task OnAfterHandleAsync_WithAMappedFailure_SeesTheFailureResult()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<TracingEndpoint>(new EndpointMetadata(["GET"], "/trace"));

        // The invoker maps a failed dispatch itself and never calls the success factory, which is
        // exactly the path OnAfterHandleAsync must still see.
        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult<IResult>(Results.StatusCode(404)));

        // TracingEndpoint.Configure always registers both processors, so they must resolve even
        // though this test's focus is the after-hook seeing the mapped failure.
        var context = ContextWith(services =>
        {
            services.AddScoped<TracingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, invoker);

        var descriptor = ((EndpointBase)new TracingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<TracingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Contains("after-hook:404", Order);
    }

    [Fact]
    public async Task OnAfterHandleAsync_WithABindFailure_SeesThe400()
    {
        // Arrange: a response-header processor that skipped every 400 would be a bug, so the exit
        // steps run on the bind-failure path too.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<BindFailingEndpoint>(
            new EndpointMetadata(["GET"], "/bind-fail"));

        var context = ContextWith(services => services.AddScoped<TracingPostProcessor>(), Ok());

        var descriptor = ((EndpointBase)new BindFailingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<BindFailingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("bind-failed-hook", Order);
        Assert.Contains("after-hook:400", Order);
        Assert.Contains("post-processor", Order);
    }

    [Fact]
    public async Task OnBindFailedAsync_ReturningAResult_ReplacesThe400()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<BindReplacingEndpoint>(
            new EndpointMetadata(["GET"], "/bind-replace"));

        var context = ContextWith(_ => { }, Ok());

        var descriptor = ((EndpointBase)new BindReplacingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<BindReplacingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
    }

    [Fact]
    public async Task PostProcessor_SettingAHeader_WritesItToTheResponse()
    {
        // Arrange: the result has not executed yet, so a header set in step 7 still reaches the wire.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<HeaderStampingEndpoint>(
            new EndpointMetadata(["GET"], "/stamp"));

        var context = ContextWith(services => services.AddScoped<HeaderStampingPostProcessor>(), Ok());

        var descriptor = ((EndpointBase)new HeaderStampingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<HeaderStampingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal("tenant-1", context.Response.Headers["X-Tenant"]);
    }

    [Fact]
    public async Task PreProcessor_ResolvingAScopedDependency_ResolvesFromTheRequestsScope()
    {
        // Arrange: BuildServiceProvider() alone lets a scoped dependency resolve silently from the
        // root provider and hand back a root-lifetime instance, so that would pass whether the
        // resolver reads context.RequestServices or a captured root container. Creating a real scope
        // and assigning ITS provider to RequestServices is the only way to tell the two apart.
        Order.Clear();
        ScopeCapturingPreProcessor.Reset();
        EndpointRegistry.RegisterMetadata<ScopeCapturingEndpoint>(new EndpointMetadata(["GET"], "/scope"));

        var services = new ServiceCollection();
        services.AddSingleton(Ok());
        services.AddLogging();
        services.AddScoped<ScopeMarker>();
        services.AddScoped<ScopeCapturingPreProcessor>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var expectedMarker = scope.ServiceProvider.GetRequiredService<ScopeMarker>();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            Response = { Body = new MemoryStream() }
        };

        var descriptor = ((EndpointBase)new ScopeCapturingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<ScopeCapturingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Same(context, ScopeCapturingPreProcessor.CapturedContext);
        Assert.Same(expectedMarker, ScopeCapturingPreProcessor.CapturedMarker);
    }

    [Fact]
    public async Task HandleAsync_OnTheVoidTier_RunsTheHooksAndProcessorsInOrder()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<VoidTracingEndpoint>(
            new EndpointMetadata(["POST"], "/void-trace"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<TraceCommand>(), Arg.Any<Func<IResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Order.Add("dispatch");
                return ValueTask.FromResult(call.Arg<Func<IResult>>()());
            });

        var context = ContextWith(services =>
        {
            services.AddScoped<TracingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, invoker);

        var descriptor = ((EndpointBase)new VoidTracingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<VoidTracingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(
            ["pre-processor", "bind", "before-hook", "dispatch", "after-hook", "post-processor"],
            Order);
    }

    [Fact]
    public async Task HandleAsync_OnTheVoidTier_RunsTheExitStepsOnABindFailure()
    {
        // Arrange: a hand-edit that dropped FinishAsync from this tier's bind-failure branch would
        // be a silent behaviour change with nothing else to catch it.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<VoidBindFailingEndpoint>(
            new EndpointMetadata(["POST"], "/void-bind-fail"));

        var context = ContextWith(services => services.AddScoped<TracingPostProcessor>(),
            Substitute.For<IHttpInvoker>());

        var descriptor = ((EndpointBase)new VoidBindFailingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<VoidBindFailingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("bind-failed-hook", Order);
        Assert.Contains("after-hook:400", Order);
        Assert.Contains("post-processor", Order);
    }

    [Fact]
    public async Task HandleAsync_OnTheVoidTier_RunsTheExitStepsOnAPreProcessorShortCircuit()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<VoidRejectingEndpoint>(
            new EndpointMetadata(["POST"], "/void-reject"));

        var context = ContextWith(services =>
        {
            services.AddScoped<RejectingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, Substitute.For<IHttpInvoker>());

        var descriptor = ((EndpointBase)new VoidRejectingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<VoidRejectingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.DoesNotContain("bind", Order);
        Assert.Contains("after-hook", Order);
        Assert.Contains("post-processor", Order);
    }

    [Fact]
    public async Task HandleAsync_OnTheMappedTier_HandsTheBeforeHookTheWireDto()
    {
        // Arrange: the hook runs around binding, and binding is what produces a DTO — so it sees
        // THttpRequest, not the message ToRequest maps it onto.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<MappedTracingEndpoint>(
            new EndpointMetadata(["POST"], "/mapped-trace/{id}"));

        var context = ContextWith(services =>
        {
            services.AddScoped<TracingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, Ok());

        context.Request.RouteValues["id"] = "wire-1";

        var descriptor = ((EndpointBase)new MappedTracingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<MappedTracingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        // No "bind" entry: this tier's binding is generated, so nothing appends to Order as it
        // runs. That binding sits between the pre-processor and the before-hook is asserted on the
        // hand-written tier instead (HandleAsync_WithEveryHookAndProcessor_RunsTheDocumentedOrder),
        // over the same BoundEndpoint.RunLifecycleAsync every tier shares.
        Assert.Equal(
            ["pre-processor", "before-hook:wire-1", "dispatch", "after-hook", "post-processor"],
            Order);
    }

    [Fact]
    public async Task HandleAsync_OnTheMappedTier_RunsTheExitStepsOnABindFailure()
    {
        // Arrange: same regression guard as the void tier, for the tier whose bound type is the
        // wire DTO rather than the message. The failure is a real one — this endpoint binds
        // TraceWireRequest from the JSON body of a POST, and the request below carries none.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<MappedBindFailingEndpoint>(
            new EndpointMetadata(["POST"], "/mapped-bind-fail"));

        var context = ContextWith(services => services.AddScoped<TracingPostProcessor>(), Ok());

        var descriptor = ((EndpointBase)new MappedBindFailingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<MappedBindFailingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("bind-failed-hook", Order);
        Assert.Contains("after-hook:400", Order);
        Assert.Contains("post-processor", Order);
    }

    [Fact]
    public async Task HandleAsync_OnTheMappedTier_RunsTheExitStepsOnAPreProcessorShortCircuit()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<MappedRejectingEndpoint>(
            new EndpointMetadata(["POST"], "/mapped-reject"));

        var context = ContextWith(services =>
        {
            services.AddScoped<RejectingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, Ok());

        var descriptor = ((EndpointBase)new MappedRejectingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<MappedRejectingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert — the 403 is itself the evidence that binding did not run: this endpoint binds its
        // wire DTO from a JSON body the request does not carry, so a binding that ran would have
        // answered 400 instead.
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Contains("after-hook", Order);
        Assert.Contains("post-processor", Order);
    }

    [Fact]
    public async Task HandleAsync_OnTheStreamTier_RunsTheExitStepsBeforeTheFirstItem()
    {
        // Arrange: a stream's result is the negotiated writer, so a post-processor can still set a
        // header — it runs before the writer executes.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<StreamTracingEndpoint>(
            new EndpointMetadata(["GET"], "/stream-trace"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeStreamAsync(Arg.Any<IStreamRequest<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Items());

        var context = ContextWith(services =>
        {
            services.AddScoped<TracingPreProcessor>();
            services.AddScoped<HeaderStampingPostProcessor>();
        }, invoker);

        var descriptor = ((EndpointBase)new StreamTracingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<StreamTracingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        // No "bind" entry — see HandleAsync_OnTheMappedTier_HandsTheBeforeHookTheWireDto.
        Assert.Equal(["pre-processor", "before-hook", "after-hook"], Order);
        Assert.Equal("tenant-1", context.Response.Headers["X-Tenant"]);

        static async IAsyncEnumerable<string> Items()
        {
            await Task.Yield();
            yield return "one";
        }
    }

    [Fact]
    public async Task HandleAsync_OnTheStreamTier_RunsTheExitStepsOnABindFailure()
    {
        // Arrange: same regression guard as the other tiers, for the tier whose result is the
        // negotiated writer rather than a value FinishAsync's post-processors can inspect further.
        // The failure is a real one: the route declares a Guid and the request sends a segment that
        // is not one, which is the only way to make a generated binding fail on a tier with no
        // hand-written equivalent.
        Order.Clear();
        EndpointRegistry.RegisterMetadata<StreamBindFailingEndpoint>(
            new EndpointMetadata(["GET"], "/stream-bind-fail/{id}"));

        var context = ContextWith(services => services.AddScoped<TracingPostProcessor>(),
            Substitute.For<IHttpInvoker>());

        context.Request.RouteValues["id"] = "not-a-guid";

        var descriptor = ((EndpointBase)new StreamBindFailingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<StreamBindFailingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("bind-failed-hook", Order);
        Assert.Contains("after-hook:400", Order);
        Assert.Contains("post-processor", Order);
    }

    [Fact]
    public async Task HandleAsync_OnTheStreamTier_RunsTheExitStepsOnAPreProcessorShortCircuit()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterMetadata<StreamRejectingEndpoint>(
            new EndpointMetadata(["GET"], "/stream-reject"));

        var context = ContextWith(services =>
        {
            services.AddScoped<RejectingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, Substitute.For<IHttpInvoker>());

        var descriptor = ((EndpointBase)new StreamRejectingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<StreamRejectingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert — "bind" is no longer in Order for either outcome on this tier, so the exit steps
        // are what this pins; that a short circuit skips binding is asserted on the hand-written
        // tier, whose BindAsync can record itself.
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Contains("after-hook", Order);
        Assert.Contains("post-processor", Order);
    }

    /// <summary>An invoker that calls the success factory with a fixed response.</summary>
    private static IHttpInvoker Ok()
    {
        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Order.Add("dispatch");
                return ValueTask.FromResult(call.Arg<Func<string, IResult>>()("hello"));
            });
        return invoker;
    }

    private static DefaultHttpContext ContextWith(Action<IServiceCollection> configure,
        IHttpInvoker invoker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        configure(services);

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    internal sealed record TraceQuery : IRequest<string>;

    /// <summary>
    ///     Sits at the hand-written-binding tier so that binding itself can be recorded. The order
    ///     under test belongs to <c>BoundEndpoint</c> and is identical for both tiers —
    ///     <c>Endpoint&lt;TRequest,TResponse&gt;</c> is an empty marker over this class — but only a
    ///     hand-written <c>BindAsync</c> can append to <c>Order</c> as it runs.
    /// </summary>
    [Get("/trace")]
    internal sealed partial class TracingEndpoint : RawEndpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PreProcessor<TracingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
        }

        public override ValueTask<BindResult<TraceQuery>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return new(BindResult<TraceQuery>.Success(new TraceQuery()));
        }

        protected override ValueTask<IResult?> OnBeforeHandleAsync(TraceQuery request,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("before-hook");
            return default;
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            // Records the status too, so the same endpoint proves both the order and that a mapped
            // failure reaches this hook.
            Order.Add($"after-hook:{StatusOf(result)}");
            return new ValueTask<IResult>(result);
        }
    }

    private sealed class ScopeMarker;

    private sealed class ScopeCapturingPreProcessor : IEndpointPreProcessor
    {
        private readonly ScopeMarker _marker;

        public ScopeCapturingPreProcessor(ScopeMarker marker)
        {
            _marker = marker;
        }

        internal static HttpContext? CapturedContext { get; private set; }
        internal static ScopeMarker? CapturedMarker { get; private set; }

        public ValueTask<IResult?> ProcessAsync(HttpContext context, CancellationToken cancellationToken)
        {
            CapturedContext = context;
            CapturedMarker = _marker;
            return default;
        }

        internal static void Reset()
        {
            CapturedContext = null;
            CapturedMarker = null;
        }
    }

    internal sealed partial class ScopeCapturingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PreProcessor<ScopeCapturingPreProcessor>();
        }
    }

    internal sealed partial class ShortCircuitingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PostProcessor<TracingPostProcessor>();
        }

        protected override ValueTask<IResult?> OnBeforeHandleAsync(TraceQuery request,
            HttpContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<IResult?>(Results.StatusCode(StatusCodes.Status409Conflict));
        }
    }

    internal sealed partial class RejectingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PreProcessor<RejectingPreProcessor>();
        }
    }

    /// <summary>
    ///     A hand-written failing binding. Was a stubbed binder registered against
    ///     Endpoint&lt;TraceQuery, string&gt;; the binding is generated now, so the failure is
    ///     expressed at the tier that exists for hand-written binding.
    /// </summary>
    [Get("/bind-fail")]
    internal sealed partial class BindFailingEndpoint : RawEndpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PostProcessor<TracingPostProcessor>();
        }

        public override ValueTask<BindResult<TraceQuery>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return new(BindResult<TraceQuery>.Failure("id", "is required."));
        }

        protected override ValueTask<IResult> OnBindFailedAsync(BindResult<TraceQuery> bound,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("bind-failed-hook");
            return base.OnBindFailedAsync(bound, context, cancellationToken);
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add($"after-hook:{StatusOf(result)}");
            return new ValueTask<IResult>(result);
        }
    }

    /// <summary>See <see cref="BindFailingEndpoint" /> for why this tier.</summary>
    [Get("/bind-replace")]
    internal sealed partial class BindReplacingEndpoint : RawEndpoint<TraceQuery, string>
    {
        public override ValueTask<BindResult<TraceQuery>> BindAsync(HttpContext context)
        {
            return new(BindResult<TraceQuery>.Failure("id", "is required."));
        }

        protected override ValueTask<IResult> OnBindFailedAsync(BindResult<TraceQuery> bound,
            HttpContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<IResult>(
                Results.StatusCode(StatusCodes.Status422UnprocessableEntity));
        }
    }

    internal sealed partial class HeaderStampingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PostProcessor<HeaderStampingPostProcessor>();
        }
    }

    internal sealed record TraceCommand : IRequest;

    /// <summary>See <see cref="TracingEndpoint" /> for why this tier.</summary>
    [Post("/void-trace")]
    internal sealed partial class VoidTracingEndpoint : RawEndpoint<TraceCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.PreProcessor<TracingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
        }

        public override ValueTask<BindResult<TraceCommand>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return new(BindResult<TraceCommand>.Success(new TraceCommand()));
        }

        protected override ValueTask<IResult?> OnBeforeHandleAsync(TraceCommand request,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("before-hook");
            return default;
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("after-hook");
            return new ValueTask<IResult>(result);
        }
    }

    /// <summary>See <see cref="BindFailingEndpoint" /> for why this tier.</summary>
    [Post("/void-bind-fail")]
    internal sealed partial class VoidBindFailingEndpoint : RawEndpoint<TraceCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.PostProcessor<TracingPostProcessor>();
        }

        public override ValueTask<BindResult<TraceCommand>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return new(BindResult<TraceCommand>.Failure("id", "is required."));
        }

        protected override ValueTask<IResult> OnBindFailedAsync(BindResult<TraceCommand> bound,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("bind-failed-hook");
            return base.OnBindFailedAsync(bound, context, cancellationToken);
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add($"after-hook:{StatusOf(result)}");
            return new ValueTask<IResult>(result);
        }
    }

    internal sealed partial class VoidRejectingEndpoint : Endpoint<TraceCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.PreProcessor<RejectingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("after-hook");
            return new ValueTask<IResult>(result);
        }
    }

    internal sealed record TraceWireRequest
    {
        public string Id { get; init; } = "wire-1";
    }

    // The route carries the wire DTO's Id, so the generated binding produces a DTO with a value this
    // test can recognise in the hook. A bodyless POST could not: on a body-carrying verb an
    // unannotated property binds from the body, and this test sends none.
    [Post("/mapped-trace/{id}")]
    internal sealed partial class MappedTracingEndpoint
        : MappedEndpoint<TraceWireRequest, TraceQuery, string, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PreProcessor<TracingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
        }

        public override TraceQuery ToRequest(TraceWireRequest request)
        {
            return new TraceQuery();
        }

        public override string ToResponse(string response)
        {
            return response;
        }

        protected override ValueTask<IResult?> OnBeforeHandleAsync(TraceWireRequest request,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add($"before-hook:{request.Id}");
            return default;
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("after-hook");
            return new ValueTask<IResult>(result);
        }
    }

    [Post("/mapped-bind-fail")]
    internal sealed partial class MappedBindFailingEndpoint
        : MappedEndpoint<TraceWireRequest, TraceQuery, string, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PostProcessor<TracingPostProcessor>();
        }

        public override TraceQuery ToRequest(TraceWireRequest request)
        {
            return new TraceQuery();
        }

        public override string ToResponse(string response)
        {
            return response;
        }

        protected override ValueTask<IResult> OnBindFailedAsync(BindResult<TraceWireRequest> bound,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("bind-failed-hook");
            return base.OnBindFailedAsync(bound, context, cancellationToken);
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add($"after-hook:{StatusOf(result)}");
            return new ValueTask<IResult>(result);
        }
    }

    [Post("/mapped-reject")]
    internal sealed partial class MappedRejectingEndpoint
        : MappedEndpoint<TraceWireRequest, TraceQuery, string, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PreProcessor<RejectingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
        }

        public override TraceQuery ToRequest(TraceWireRequest request)
        {
            return new TraceQuery();
        }

        public override string ToResponse(string response)
        {
            return response;
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("after-hook");
            return new ValueTask<IResult>(result);
        }
    }

    internal sealed record TraceStream : IStreamRequest<string>;

    internal sealed partial class StreamTracingEndpoint : StreamEndpoint<TraceStream, string>
    {
        public override void Configure(IStreamEndpointBuilder builder)
        {
            builder.PreProcessor<TracingPreProcessor>()
                   .PostProcessor<HeaderStampingPostProcessor>();
        }

        protected override ValueTask<IResult?> OnBeforeHandleAsync(TraceStream request,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("before-hook");
            return default;
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("after-hook");
            return new ValueTask<IResult>(result);
        }
    }

    /// <summary>
    ///     Streams a request carrying a <see cref="System.Guid" /> route value, so a non-GUID segment
    ///     makes its generated binding fail. Its own message type, not <c>TraceStream</c>: the other
    ///     two stream fixtures need a binding that succeeds with no request input at all.
    /// </summary>
    internal sealed record TraceStreamById(Guid Id) : IStreamRequest<string>;

    [Get("/stream-bind-fail/{id}")]
    internal sealed partial class StreamBindFailingEndpoint : StreamEndpoint<TraceStreamById, string>
    {
        public override void Configure(IStreamEndpointBuilder builder)
        {
            builder.PostProcessor<TracingPostProcessor>();
        }

        protected override ValueTask<IResult> OnBindFailedAsync(BindResult<TraceStreamById> bound,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("bind-failed-hook");
            return base.OnBindFailedAsync(bound, context, cancellationToken);
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add($"after-hook:{StatusOf(result)}");
            return new ValueTask<IResult>(result);
        }
    }

    internal sealed partial class StreamRejectingEndpoint : StreamEndpoint<TraceStream, string>
    {
        public override void Configure(IStreamEndpointBuilder builder)
        {
            builder.PreProcessor<RejectingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
        }

        protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
            HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("after-hook");
            return new ValueTask<IResult>(result);
        }
    }

    private sealed class TracingPreProcessor : IEndpointPreProcessor
    {
        public ValueTask<IResult?> ProcessAsync(HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("pre-processor");
            return default;
        }
    }

    private sealed class RejectingPreProcessor : IEndpointPreProcessor
    {
        public ValueTask<IResult?> ProcessAsync(HttpContext context, CancellationToken cancellationToken)
        {
            Order.Add("pre-processor");
            return new ValueTask<IResult?>(Results.StatusCode(StatusCodes.Status403Forbidden));
        }
    }

    private sealed class TracingPostProcessor : IEndpointPostProcessor
    {
        public ValueTask<IResult> ProcessAsync(IResult result, HttpContext context,
            CancellationToken cancellationToken)
        {
            Order.Add("post-processor");
            return new ValueTask<IResult>(result);
        }
    }

    private sealed class HeaderStampingPostProcessor : IEndpointPostProcessor
    {
        public ValueTask<IResult> ProcessAsync(IResult result, HttpContext context,
            CancellationToken cancellationToken)
        {
            context.Response.Headers["X-Tenant"] = "tenant-1";
            return new ValueTask<IResult>(result);
        }
    }

    /// <summary>Reads the status a result carries, for asserting what a hook was handed.</summary>
    private static int StatusOf(IResult result)
    {
        return result switch
        {
            IStatusCodeHttpResult status => status.StatusCode ?? 0,
            _ => 0
        };
    }
}
