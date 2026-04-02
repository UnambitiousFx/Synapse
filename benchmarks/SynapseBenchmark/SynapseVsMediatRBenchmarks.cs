using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using IRequest = MediatR.IRequest;

namespace UnambitiousFx.Benchmarks.SynapseBenchmark;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[HideColumns(Column.RatioSD, Column.StdDev)]
public class SynapseVsMediatRBenchmarks
{
    private static readonly RequestWithResponse RrRequest = new(41, 1);
    private static readonly RequestWithoutResponse RqRequest = new();
    private static readonly SynapseEvent SynapseEvt = new();

    private static readonly MediatRRequestWithResponse MrRrRequest = new(41, 1);
    private static readonly MediatRRequestWithoutResponse MrRqRequest = new();
    private static readonly MrNotification MrEvt = new();

    private IMediator _mrMediatorBase = default!;
    private IMediator _mrMediator1Beh = default!;
    private IMediator _mrMediator3Beh = default!;

    private IEmitter _synapseEmitterBase = default!;
    private IInvoker _synapseInvokerBase = default!;
    private IInvoker _synapseInvoker1Beh = default!;
    private IInvoker _synapseInvoker3Beh = default!;
    private RequestWithResponseHandler _synapseDirectHandlerRr = default!;
    private RequestWithoutResponseHandler _synapseDirectHandlerVoid = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var ourBaseSp = BuildOurSp(0, true);
        var our1BehSp = BuildOurSp(1, true);
        var our3BehSp = BuildOurSp(3, true);

        _synapseInvokerBase = ourBaseSp.GetRequiredService<IInvoker>();
        _synapseEmitterBase = ourBaseSp.GetRequiredService<IEmitter>();
        _synapseInvoker1Beh = our1BehSp.GetRequiredService<IInvoker>();
        _synapseInvoker3Beh = our3BehSp.GetRequiredService<IInvoker>();
        _synapseDirectHandlerRr = ourBaseSp.GetRequiredService<RequestWithResponseHandler>();
        _synapseDirectHandlerVoid = ourBaseSp.GetRequiredService<RequestWithoutResponseHandler>();

        var mrBaseSp = BuildMediatRSp(0);
        var mr1BehSp = BuildMediatRSp(1);
        var mr3BehSp = BuildMediatRSp(3);

        _mrMediatorBase = mrBaseSp.GetRequiredService<IMediator>();
        _mrMediator1Beh = mr1BehSp.GetRequiredService<IMediator>();
        _mrMediator3Beh = mr3BehSp.GetRequiredService<IMediator>();
    }

    private static IServiceProvider BuildOurSp(int behaviors, bool slimContext)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<RequestWithResponseHandler, RequestWithResponse, int>();
            cfg.RegisterRequestHandler<RequestWithoutResponseHandler, RequestWithoutResponse>();

            // Event handlers
            cfg.RegisterEventHandler<OurEventHandler1, SynapseEvent>();
            cfg.RegisterEventHandler<OurEventHandler2, SynapseEvent>();
            cfg.RegisterEventHandler<OurEventHandler3, SynapseEvent>();
            cfg.RegisterEventHandler<OurEventHandler4, SynapseEvent>();
            cfg.RegisterEventHandler<OurEventHandler5, SynapseEvent>();

            // Request pipeline behaviors (untyped so they apply to both)
            if (behaviors >= 1)
            {
                cfg.RegisterRequestPipelineBehavior<OurNoOpBehavior1>();
            }

            if (behaviors >= 2)
            {
                cfg.RegisterRequestPipelineBehavior<OurNoOpBehavior2>();
            }

            if (behaviors >= 3)
            {
                cfg.RegisterRequestPipelineBehavior<OurNoOpBehavior3>();
            }

            if (slimContext)
            {
                cfg.UseSlimContextFactory();
            }
            else
            {
                cfg.UseDefaultContextFactory();
            }
        });
        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildMediatRSp(int behaviors)
    {
        var services = new ServiceCollection();
        // MediatR v13 performs a license check that resolves ILoggerFactory; register minimal no-op logging to satisfy DI
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MediatRRequestWithResponse>());

        // Explicitly register handlers in case assembly scanning misses nested types or file location
        services.AddTransient(typeof(MediatR.IRequestHandler<MediatRRequestWithResponse, int>),
            typeof(MediatRRequestWithResponseHandler));
        services.AddTransient(typeof(MediatR.IRequestHandler<MediatRRequestWithoutResponse>),
            typeof(MediatRRequestWithoutResponseHandler));

        // Event handlers
        services.AddTransient<INotificationHandler<MrNotification>, MrNotificationHandler1>();
        services.AddTransient<INotificationHandler<MrNotification>, MrNotificationHandler2>();
        services.AddTransient<INotificationHandler<MrNotification>, MrNotificationHandler3>();
        services.AddTransient<INotificationHandler<MrNotification>, MrNotificationHandler4>();
        services.AddTransient<INotificationHandler<MrNotification>, MrNotificationHandler5>();

        // Pipeline behaviors
        if (behaviors >= 1)
        {
            services.AddTransient(typeof(IPipelineBehavior<MediatRRequestWithResponse, int>),
                typeof(MrNoOpBehavior1_RR));
            services.AddTransient(typeof(IPipelineBehavior<MediatRRequestWithoutResponse, Unit>),
                typeof(MrNoOpBehavior1_Void));
        }

        if (behaviors >= 2)
        {
            services.AddTransient(typeof(IPipelineBehavior<MediatRRequestWithResponse, int>),
                typeof(MrNoOpBehavior2_RR));
            services.AddTransient(typeof(IPipelineBehavior<MediatRRequestWithoutResponse, Unit>),
                typeof(MrNoOpBehavior2_Void));
        }

        if (behaviors >= 3)
        {
            services.AddTransient(typeof(IPipelineBehavior<MediatRRequestWithResponse, int>),
                typeof(MrNoOpBehavior3_RR));
            services.AddTransient(typeof(IPipelineBehavior<MediatRRequestWithoutResponse, Unit>),
                typeof(MrNoOpBehavior3_Void));
        }

        return services.BuildServiceProvider();
    }

    // ── Send with response ───────────────────────────────────────────────────

    [BenchmarkCategory("Send (response)"), Benchmark(Baseline = true, Description = "Synapse")]
    public async Task<int> Synapse_Send_Response()
    {
        var res = await _synapseInvokerBase.InvokeAsync(RrRequest);
        return res.TryGet(out var v, out _) ? v! : -1;
    }

    [BenchmarkCategory("Send (response)"), Benchmark(Description = "MediatR")]
    public Task<int> MediatR_Send_Response()
    {
        return _mrMediatorBase.Send(MrRrRequest);
    }

    [BenchmarkCategory("Send (response)"), Benchmark(Description = "Synapse (direct handler)")]
    public async Task<int> Synapse_Send_Response_Direct()
    {
        var res = await _synapseDirectHandlerRr.HandleAsync(RrRequest, CancellationToken.None);
        return res.TryGet(out var v, out _) ? v! : -1;
    }

    // ── Send void ────────────────────────────────────────────────────────────

    [BenchmarkCategory("Send (void)"), Benchmark(Baseline = true, Description = "Synapse")]
    public async Task<bool> Synapse_Send_Void()
    {
        var res = await _synapseInvokerBase.InvokeAsync(RqRequest);
        return res.IsSuccess;
    }

    [BenchmarkCategory("Send (void)"), Benchmark(Description = "MediatR")]
    public async Task<bool> MediatR_Send_Void()
    {
        await _mrMediatorBase.Send(MrRqRequest);
        return true;
    }

    [BenchmarkCategory("Send (void)"), Benchmark(Description = "Synapse (direct handler)")]
    public async Task<bool> Synapse_Send_Void_Direct()
    {
        var res = await _synapseDirectHandlerVoid.HandleAsync(RqRequest, CancellationToken.None);
        return res.IsSuccess;
    }

    // ── Publish (5 handlers) ─────────────────────────────────────────────────

    [BenchmarkCategory("Publish (5 handlers)"), Benchmark(Baseline = true, Description = "Synapse")]
    public async Task<bool> Synapse_Publish_5Handlers()
    {
        var res = await _synapseEmitterBase.EmitAsync(SynapseEvt);
        return res.IsSuccess;
    }

    [BenchmarkCategory("Publish (5 handlers)"), Benchmark(Description = "MediatR")]
    public Task MediatR_Publish_5Handlers()
    {
        return _mrMediatorBase.Publish(MrEvt);
    }

    // ── Pipeline: 1 behavior ─────────────────────────────────────────────────

    [BenchmarkCategory("Pipeline (1 behavior)"), Benchmark(Baseline = true, Description = "Synapse")]
    public async Task<int> Synapse_Send_1Behavior()
    {
        var res = await _synapseInvoker1Beh.InvokeAsync(RrRequest);
        return res.TryGet(out var v, out _) ? v! : -1;
    }

    [BenchmarkCategory("Pipeline (1 behavior)"), Benchmark(Description = "MediatR")]
    public Task<int> MediatR_Send_1Behavior()
    {
        return _mrMediator1Beh.Send(MrRrRequest);
    }

    // ── Pipeline: 3 behaviors ────────────────────────────────────────────────

    [BenchmarkCategory("Pipeline (3 behaviors)"), Benchmark(Baseline = true, Description = "Synapse")]
    public async Task<int> Synapse_Send_3Behaviors()
    {
        var res = await _synapseInvoker3Beh.InvokeAsync(RrRequest);
        return res.TryGet(out var v, out _) ? v! : -1;
    }

    [BenchmarkCategory("Pipeline (3 behaviors)"), Benchmark(Description = "MediatR")]
    public Task<int> MediatR_Send_3Behaviors()
    {
        return _mrMediator3Beh.Send(MrRrRequest);
    }

    // ===== Types for Our Mediator =====
    public sealed record RequestWithResponse(
        int A,
        int B) : Synapse.Abstractions.IRequest<int>;

    public sealed record RequestWithoutResponse : Synapse.Abstractions.IRequest;

    public sealed class
        RequestWithResponseHandler : Synapse.Abstractions.IRequestHandler<RequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(RequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success(request.A + request.B));
        }
    }

    public sealed class
        RequestWithoutResponseHandler : Synapse.Abstractions.IRequestHandler<RequestWithoutResponse>
    {
        public ValueTask<Result> HandleAsync(RequestWithoutResponse request,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    public sealed class OurNoOpBehavior1 : IRequestPipelineBehavior
    {
        public ValueTask<Result> HandleAsync<TRequest>(TRequest request,
            RequestHandlerDelegate next,
            CancellationToken cancellationToken = default)
            where TRequest : Synapse.Abstractions.IRequest
        {
            return next();
        }

        public ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
            Synapse.Abstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
            where TRequest : Synapse.Abstractions.IRequest<TResponse>
        {
            return next();
        }
    }

    public sealed class OurNoOpBehavior2 : IRequestPipelineBehavior
    {
        public ValueTask<Result> HandleAsync<TRequest>(TRequest request,
            RequestHandlerDelegate next,
            CancellationToken cancellationToken = default)
            where TRequest : Synapse.Abstractions.IRequest
        {
            return next();
        }

        public ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
            Synapse.Abstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
            where TRequest : Synapse.Abstractions.IRequest<TResponse>
        {
            return next();
        }
    }

    public sealed class OurNoOpBehavior3 : IRequestPipelineBehavior
    {
        public ValueTask<Result> HandleAsync<TRequest>(TRequest request,
            RequestHandlerDelegate next,
            CancellationToken cancellationToken = default)
            where TRequest : Synapse.Abstractions.IRequest
        {
            return next();
        }

        public ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
            Synapse.Abstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
            where TRequest : Synapse.Abstractions.IRequest<TResponse>
        {
            return next();
        }
    }

    public sealed class SynapseEvent : IEvent
    {
    }

    public sealed class OurEventHandler1 : IEventHandler<SynapseEvent>
    {
        public ValueTask<Result> HandleAsync(SynapseEvent @event,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    public sealed class OurEventHandler2 : IEventHandler<SynapseEvent>
    {
        public ValueTask<Result> HandleAsync(SynapseEvent @event,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    public sealed class OurEventHandler3 : IEventHandler<SynapseEvent>
    {
        public ValueTask<Result> HandleAsync(SynapseEvent @event,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    public sealed class OurEventHandler4 : IEventHandler<SynapseEvent>
    {
        public ValueTask<Result> HandleAsync(SynapseEvent @event,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    public sealed class OurEventHandler5 : IEventHandler<SynapseEvent>
    {
        public ValueTask<Result> HandleAsync(SynapseEvent @event,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    // ===== Types for MediatR =====
    public sealed record MediatRRequestWithResponse(
        int A,
        int B) : MediatR.IRequest<int>;

    public sealed record MediatRRequestWithoutResponse : IRequest;

    public sealed class MediatRRequestWithResponseHandler : MediatR.IRequestHandler<MediatRRequestWithResponse, int>
    {
        public Task<int> Handle(MediatRRequestWithResponse request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(request.A + request.B);
        }
    }

    public sealed class MediatRRequestWithoutResponseHandler : MediatR.IRequestHandler<MediatRRequestWithoutResponse>
    {
        public Task Handle(MediatRRequestWithoutResponse request,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class MrNoOpBehavior1_RR : IPipelineBehavior<MediatRRequestWithResponse, int>
    {
        public Task<int> Handle(MediatRRequestWithResponse request, MediatR.RequestHandlerDelegate<int> next,
            CancellationToken cancellationToken)
        {
            return next();
        }
    }

    public sealed class MrNoOpBehavior2_RR : IPipelineBehavior<MediatRRequestWithResponse, int>
    {
        public Task<int> Handle(MediatRRequestWithResponse request, MediatR.RequestHandlerDelegate<int> next,
            CancellationToken cancellationToken)
        {
            return next();
        }
    }

    public sealed class MrNoOpBehavior3_RR : IPipelineBehavior<MediatRRequestWithResponse, int>
    {
        public Task<int> Handle(MediatRRequestWithResponse request, MediatR.RequestHandlerDelegate<int> next,
            CancellationToken cancellationToken)
        {
            return next();
        }
    }

    public sealed class MrNoOpBehavior1_Void : IPipelineBehavior<MediatRRequestWithoutResponse, Unit>
    {
        public Task<Unit> Handle(MediatRRequestWithoutResponse request, MediatR.RequestHandlerDelegate<Unit> next,
            CancellationToken cancellationToken)
        {
            return next();
        }
    }

    public sealed class MrNoOpBehavior2_Void : IPipelineBehavior<MediatRRequestWithoutResponse, Unit>
    {
        public Task<Unit> Handle(MediatRRequestWithoutResponse request, MediatR.RequestHandlerDelegate<Unit> next,
            CancellationToken cancellationToken)
        {
            return next();
        }
    }

    public sealed class MrNoOpBehavior3_Void : IPipelineBehavior<MediatRRequestWithoutResponse, Unit>
    {
        public Task<Unit> Handle(MediatRRequestWithoutResponse request, MediatR.RequestHandlerDelegate<Unit> next,
            CancellationToken cancellationToken)
        {
            return next();
        }
    }

    public sealed class MrNotification : INotification
    {
    }

    public sealed class MrNotificationHandler1 : INotificationHandler<MrNotification>
    {
        public Task Handle(MrNotification notification,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class MrNotificationHandler2 : INotificationHandler<MrNotification>
    {
        public Task Handle(MrNotification notification,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class MrNotificationHandler3 : INotificationHandler<MrNotification>
    {
        public Task Handle(MrNotification notification,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class MrNotificationHandler4 : INotificationHandler<MrNotification>
    {
        public Task Handle(MrNotification notification,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class MrNotificationHandler5 : INotificationHandler<MrNotification>
    {
        public Task Handle(MrNotification notification,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}