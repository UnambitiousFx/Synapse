using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class EndpointLifecycleTests
{
    private static readonly List<string> Order = [];

    [Fact]
    public async Task HandleAsync_WithEveryHookAndProcessor_RunsInTheDocumentedOrder()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterBinder(new TracingBinder());
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
        EndpointRegistry.RegisterBinder(new TracingBinder());
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
        EndpointRegistry.RegisterBinder(new TracingBinder());
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
        EndpointRegistry.RegisterBinder(new TracingBinder());
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
        EndpointRegistry.RegisterBinder(new FailingTracingBinder());
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
        EndpointRegistry.RegisterBinder(new FailingTracingBinder());
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
        EndpointRegistry.RegisterBinder(new TracingBinder());
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
    public async Task HandleAsync_OnTheVoidTier_RunsTheHooksAndProcessorsInOrder()
    {
        // Arrange
        Order.Clear();
        EndpointRegistry.RegisterBinder(new VoidTracingBinder());
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
    public async Task HandleAsync_OnTheMappedTier_HandsTheBeforeHookTheWireDto()
    {
        // Arrange: the hook runs around binding, and binding is what produces a DTO — so it sees
        // THttpRequest, not the message ToRequest maps it onto.
        Order.Clear();
        EndpointRegistry.RegisterBinder(new MappedTracingBinder());
        EndpointRegistry.RegisterMetadata<MappedTracingEndpoint>(
            new EndpointMetadata(["POST"], "/mapped-trace"));

        var context = ContextWith(services =>
        {
            services.AddScoped<TracingPreProcessor>();
            services.AddScoped<TracingPostProcessor>();
        }, Ok());

        var descriptor = ((EndpointBase)new MappedTracingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<MappedTracingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(
            ["pre-processor", "bind", "before-hook:wire-1", "dispatch", "after-hook",
             "post-processor"],
            Order);
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

    private sealed record TraceQuery : IRequest<string>;

    private sealed class TracingBinder : IEndpointBinder<TraceQuery>
    {
        public ValueTask<BindResult<TraceQuery>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return ValueTask.FromResult(BindResult<TraceQuery>.Success(new TraceQuery()));
        }
    }

    private sealed class FailingTracingBinder : IEndpointBinder<TraceQuery>
    {
        public ValueTask<BindResult<TraceQuery>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return ValueTask.FromResult(BindResult<TraceQuery>.Failure("id", "is not a valid Guid."));
        }
    }

    private sealed class TracingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PreProcessor<TracingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
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

    private sealed class ShortCircuitingEndpoint : Endpoint<TraceQuery, string>
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

    private sealed class RejectingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PreProcessor<RejectingPreProcessor>();
        }
    }

    private sealed class BindFailingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PostProcessor<TracingPostProcessor>();
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

    private sealed class BindReplacingEndpoint : Endpoint<TraceQuery, string>
    {
        protected override ValueTask<IResult> OnBindFailedAsync(BindResult<TraceQuery> bound,
            HttpContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<IResult>(
                Results.StatusCode(StatusCodes.Status422UnprocessableEntity));
        }
    }

    private sealed class HeaderStampingEndpoint : Endpoint<TraceQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.PostProcessor<HeaderStampingPostProcessor>();
        }
    }

    private sealed record TraceCommand : IRequest;

    private sealed class VoidTracingBinder : IEndpointBinder<TraceCommand>
    {
        public ValueTask<BindResult<TraceCommand>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return ValueTask.FromResult(BindResult<TraceCommand>.Success(new TraceCommand()));
        }
    }

    private sealed class VoidTracingEndpoint : Endpoint<TraceCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.PreProcessor<TracingPreProcessor>()
                   .PostProcessor<TracingPostProcessor>();
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

    private sealed record TraceWireRequest
    {
        public string Id { get; init; } = "wire-1";
    }

    private sealed class MappedTracingBinder : IEndpointBinder<TraceWireRequest>
    {
        public ValueTask<BindResult<TraceWireRequest>> BindAsync(HttpContext context)
        {
            Order.Add("bind");
            return ValueTask.FromResult(BindResult<TraceWireRequest>.Success(new TraceWireRequest()));
        }
    }

    private sealed class MappedTracingEndpoint
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
