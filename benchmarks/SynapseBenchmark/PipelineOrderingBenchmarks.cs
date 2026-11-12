using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Benchmarks.SynapseBenchmark;

/// <summary>
///     Relative behavior ordering (#162) must not cost anything per request. The same three behaviors run either
///     ordered by <see cref="IOrderedPipelineBehavior.Order" /> alone (the legacy path) or by stages and Before/After
///     constraints that reverse their numeric order (the resolver path).
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item><b>Dispatch</b>: the pipeline is already composed; this is the per-request hot path.</item>
///         <item><b>Scoped send</b>: a new DI scope per request, so the proxy and its chain are rebuilt each time, as
///             in ASP.NET Core with the default scoped handlers. The resolver path reuses its cached order here.</item>
///         <item><b>Cold start</b>: build the container and send once, which pays the one-time order resolution.</item>
///     </list>
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PipelineOrderingBenchmarks
{
    private static readonly OrderingQuery Query = new(41);

    private IRequestHandler<OrderingQuery, int> _orderOnlyHandler = null!;
    private ServiceProvider _orderOnlyProvider = null!;
    private IServiceScope _orderOnlyScope = null!;
    private IRequestHandler<OrderingQuery, int> _stagedHandler = null!;
    private ServiceProvider _stagedProvider = null!;
    private IServiceScope _stagedScope = null!;

    [GlobalSetup]
    public void Setup()
    {
        _orderOnlyProvider = Build(false);
        _stagedProvider = Build(true);
        _orderOnlyScope = _orderOnlyProvider.CreateScope();
        _stagedScope = _stagedProvider.CreateScope();
        _orderOnlyHandler = _orderOnlyScope.ServiceProvider.GetRequiredService<IRequestHandler<OrderingQuery, int>>();
        _stagedHandler = _stagedScope.ServiceProvider.GetRequiredService<IRequestHandler<OrderingQuery, int>>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _orderOnlyScope.Dispose();
        _stagedScope.Dispose();
        _orderOnlyProvider.Dispose();
        _stagedProvider.Dispose();
    }

    [BenchmarkCategory("Dispatch"), Benchmark(Baseline = true, Description = "Order only")]
    public ValueTask<Result<int>> Dispatch_OrderOnly()
    {
        return _orderOnlyHandler.HandleAsync(Query);
    }

    [BenchmarkCategory("Dispatch"), Benchmark(Description = "Stages + Before/After")]
    public ValueTask<Result<int>> Dispatch_Staged()
    {
        return _stagedHandler.HandleAsync(Query);
    }

    [BenchmarkCategory("Scoped send"), Benchmark(Baseline = true, Description = "Order only")]
    public async Task<int> ScopedSend_OrderOnly()
    {
        return await Send(_orderOnlyProvider);
    }

    [BenchmarkCategory("Scoped send"), Benchmark(Description = "Stages + Before/After")]
    public async Task<int> ScopedSend_Staged()
    {
        return await Send(_stagedProvider);
    }

    [BenchmarkCategory("Cold start"), Benchmark(Baseline = true, Description = "Order only")]
    public async Task<int> ColdStart_OrderOnly()
    {
        await using var provider = Build(false);
        return await Send(provider);
    }

    [BenchmarkCategory("Cold start"), Benchmark(Description = "Stages + Before/After")]
    public async Task<int> ColdStart_Staged()
    {
        await using var provider = Build(true);
        return await Send(provider);
    }

    private static async Task<int> Send(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IInvoker>().InvokeAsync(Query);
        return result.TryGet(out var value, out _) ? value : -1;
    }

    private static ServiceProvider Build(bool staged)
    {
        return new ServiceCollection()
            .AddLogging()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<OrderingQueryHandler, OrderingQuery, int>();
                cfg.RegisterRequestPipelineBehavior<TraceBehavior, OrderingQuery, int>();
                cfg.RegisterRequestPipelineBehavior<ActorBehavior, OrderingQuery, int>();
                cfg.RegisterRequestPipelineBehavior<AuditBehavior, OrderingQuery, int>();
                if (staged)
                {
                    // Reverses the numeric order: Audit (Order 10) ends up innermost, Trace (Order 30) outermost.
                    cfg.ConfigureBehaviorOrdering(typeof(TraceBehavior), PipelineStage.Observability);
                    cfg.ConfigureBehaviorOrdering(typeof(ActorBehavior), PipelineStage.Authorization);
                    cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior), PipelineStage.Authorization,
                        after: typeof(ActorBehavior));
                }
            })
            .BuildServiceProvider();
    }

    public sealed record OrderingQuery(int Value) : IRequest<int>;

    public sealed class OrderingQueryHandler : IRequestHandler<OrderingQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(OrderingQuery request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(request.Value + 1));
        }
    }

    public sealed class TraceBehavior : PassThrough, IOrderedPipelineBehavior
    {
        public uint Order => 30;
    }

    public sealed class ActorBehavior : PassThrough, IOrderedPipelineBehavior
    {
        public uint Order => 20;
    }

    public sealed class AuditBehavior : PassThrough, IOrderedPipelineBehavior
    {
        public uint Order => 10;
    }

    public abstract class PassThrough : IRequestPipelineBehavior<OrderingQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(OrderingQuery request,
            RequestHandlerDelegate<OrderingQuery, int> next, CancellationToken cancellationToken = default)
        {
            return next(request, cancellationToken);
        }
    }
}
