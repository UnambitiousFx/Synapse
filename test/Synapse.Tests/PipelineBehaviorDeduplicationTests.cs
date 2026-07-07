using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests;

// Regression tests for known-issue #008: an open-generic user pipeline behavior is cross-producted
// against every handler in the reference graph, so two opted-in assemblies' generated RegisterGroups
// can each emit the same closed behavior over the same request. Without dedup the behavior is
// registered — and runs — twice. Two identical RegisterGroups simulate that double opt-in.
public sealed class PipelineBehaviorDeduplicationTests
{
    [Fact]
    public void Duplicate_behavior_registration_without_response_is_deduplicated()
    {
        // Arrange (Given) — two RegisterGroups both close the same behavior over the same request.
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<DedupRequestHandler, DedupRequest>();
            cfg.AddRegisterGroup(new BehaviorGroup());
            cfg.AddRegisterGroup(new BehaviorGroup());
        });

        // Assert (Then) — only one descriptor survived for the closed behavior/request pair.
        var descriptors = services.Count(d =>
            d.ServiceType == typeof(IRequestPipelineBehavior<DedupRequest>) &&
            d.ImplementationType == typeof(CountingBehavior));
        Assert.Equal(1, descriptors);
    }

    [Fact]
    public async Task Duplicate_behavior_registration_without_response_executes_once()
    {
        // Arrange (Given)
        var counter = new ExecutionCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<DedupRequestHandler, DedupRequest>();
            cfg.AddRegisterGroup(new BehaviorGroup());
            cfg.AddRegisterGroup(new BehaviorGroup());
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act (When)
        await sender.InvokeAsync(new DedupRequest(), TestContext.Current.CancellationToken);

        // Assert (Then) — the behavior ran exactly once, not once per opted-in group.
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void Duplicate_behavior_registration_with_response_is_deduplicated()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<DedupRequestWithResponseHandler, DedupRequestWithResponse, int>();
            cfg.AddRegisterGroup(new BehaviorWithResponseGroup());
            cfg.AddRegisterGroup(new BehaviorWithResponseGroup());
        });

        // Assert (Then)
        var descriptors = services.Count(d =>
            d.ServiceType == typeof(IRequestPipelineBehavior<DedupRequestWithResponse, int>) &&
            d.ImplementationType == typeof(CountingBehaviorWithResponse));
        Assert.Equal(1, descriptors);
    }

    [Fact]
    public async Task Behavior_preregistered_via_typed_factory_is_deduplicated()
    {
        // Arrange (Given) — the same closed behavior is pre-registered on the raw collection via a typed
        // Scoped factory (no ImplementationType — the old guard could not see it), then re-registered by
        // type through the builder. The two-arg overload's declared return type is the concrete behavior,
        // which is the identity dedup now keys on.
        var counter = new ExecutionCounter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddScoped<IRequestPipelineBehavior<DedupRequest>, CountingBehavior>(sp =>
            new CountingBehavior(sp.GetRequiredService<ExecutionCounter>()));
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<DedupRequestHandler, DedupRequest>();
            cfg.AddRegisterGroup(new BehaviorGroup());
        });

        // Assert (Then) — only one descriptor survived for the behavior/request pair...
        var descriptors = services.Count(d =>
            d.ServiceType == typeof(IRequestPipelineBehavior<DedupRequest>));
        Assert.Equal(1, descriptors);

        // ...and it runs exactly once.
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IInvoker>().InvokeAsync(new DedupRequest(), TestContext.Current.CancellationToken);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task Cqrs_behavior_preregistered_via_factory_runs_once()
    {
        // Arrange (Given) — CQRS enforcement pre-registered via a typed Scoped factory (the
        // non-idempotent behavior); the builder then registers it again by type. Without factory-aware
        // dedup both are added, the second sees the first's boundary marker and throws on every request.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestPipelineBehavior<DedupRequest>, CqrsBoundaryEnforcementBehavior<DedupRequest>>(
            sp => new CqrsBoundaryEnforcementBehavior<DedupRequest>(sp.GetRequiredService<IContext>()));
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<DedupRequestHandler, DedupRequest>();
            cfg.RegisterCqrsBoundaryEnforcement<DedupRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.CreateScope().ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await sender.InvokeAsync(new DedupRequest(), TestContext.Current.CancellationToken);

        // Assert (Then) — deduped to a single instance, so no spurious boundary violation.
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Behavior_registered_with_conflicting_lifetime_throws()
    {
        // Arrange (Given) — the same closed behavior is pre-registered as Singleton; the builder
        // registers it as Scoped.
        var services = new ServiceCollection();
        services.AddSingleton<IRequestPipelineBehavior<DedupRequest>, CountingBehavior>();

        // Act (When) / Assert (Then) — the lifetime conflict is surfaced, not silently dropped.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<DedupRequestHandler, DedupRequest>();
                cfg.AddRegisterGroup(new BehaviorGroup());
            }));
        Assert.Contains(nameof(CountingBehavior), ex.Message);
        Assert.Contains("Singleton", ex.Message);
        Assert.Contains("Scoped", ex.Message);
    }

    private sealed class BehaviorGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterRequestPipelineBehavior<CountingBehavior, DedupRequest>();
        }
    }

    private sealed class BehaviorWithResponseGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterRequestPipelineBehavior<CountingBehaviorWithResponse, DedupRequestWithResponse, int>();
        }
    }

    private sealed record DedupRequest : IRequest;

    private sealed record DedupRequestWithResponse(int Value) : IRequest<int>;

    private sealed class ExecutionCounter
    {
        public int Count { get; private set; }

        public void Increment()
        {
            Count++;
        }
    }

    private sealed class DedupRequestHandler : IRequestHandler<DedupRequest>
    {
        public ValueTask<Result> HandleAsync(DedupRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class DedupRequestWithResponseHandler : IRequestHandler<DedupRequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(DedupRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(request.Value));
        }
    }

    private sealed class CountingBehavior(ExecutionCounter counter) : IRequestPipelineBehavior<DedupRequest>
    {
        public ValueTask<Result> HandleAsync(DedupRequest request, RequestHandlerDelegate<DedupRequest> next,
            CancellationToken cancellationToken = default)
        {
            counter.Increment();
            return next(request, cancellationToken);
        }
    }

    private sealed class CountingBehaviorWithResponse : IRequestPipelineBehavior<DedupRequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(DedupRequestWithResponse request,
            RequestHandlerDelegate<DedupRequestWithResponse, int> next,
            CancellationToken cancellationToken = default)
        {
            return next(request, cancellationToken);
        }
    }
}
