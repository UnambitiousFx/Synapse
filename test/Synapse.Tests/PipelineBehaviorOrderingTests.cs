using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests;

/// <summary>
///     Verifies that <see cref="IOrderedPipelineBehavior.Order" /> is honored at runtime regardless of
///     the order behaviors are registered in — the regression covered by known-issue 009.
/// </summary>
public sealed class PipelineBehaviorOrderingTests
{
    [Fact]
    public async Task Behaviors_execute_in_global_order_independent_of_registration_order()
    {
        // Arrange (Given) — register behaviors in a deliberately scrambled order. Their runtime
        // positions are First < 100 < Middle < Last(default), so execution must follow that, not the
        // registration sequence below.
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(executionOrder);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<SampleHandler, SampleRequest>();
            cfg.RegisterRequestPipelineBehavior<DefaultLastBehavior, SampleRequest>();
            cfg.RegisterRequestPipelineBehavior<MiddleBehavior, SampleRequest>();
            cfg.RegisterRequestPipelineBehavior<HundredBehavior, SampleRequest>();
            cfg.RegisterRequestPipelineBehavior<FirstBehavior, SampleRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act (When)
        await sender.InvokeAsync(new SampleRequest());

        // Assert (Then) — outermost (lowest Order) first.
        Assert.Equal(["First", "Hundred", "Middle", "DefaultLast"], executionOrder);
    }

    [Fact]
    public async Task First_behavior_wraps_a_default_behavior_even_when_registered_after_it()
    {
        // Arrange (Given) — this is the Insert(0) contention scenario: the "First" behavior is
        // registered AFTER the default one, yet must still resolve outermost.
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(executionOrder);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<SampleHandler, SampleRequest>();
            cfg.RegisterRequestPipelineBehavior<DefaultLastBehavior, SampleRequest>();
            cfg.RegisterRequestPipelineBehavior<FirstBehavior, SampleRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act (When)
        await sender.InvokeAsync(new SampleRequest());

        // Assert (Then)
        Assert.Equal(["First", "DefaultLast"], executionOrder);
    }

    private sealed record SampleRequest : IRequest;

    private sealed class SampleHandler : IRequestHandler<SampleRequest>
    {
        public ValueTask<Result> HandleAsync(SampleRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class FirstBehavior(List<string> log)
        : IRequestPipelineBehavior<SampleRequest>, IOrderedPipelineBehavior
    {
        public uint Order => IOrderedPipelineBehavior.First;

        public ValueTask<Result> HandleAsync(SampleRequest request,
            RequestHandlerDelegate<SampleRequest> next, CancellationToken cancellationToken = default)
        {
            log.Add("First");
            return next(request, cancellationToken);
        }
    }

    private sealed class HundredBehavior(List<string> log)
        : IRequestPipelineBehavior<SampleRequest>, IOrderedPipelineBehavior
    {
        public uint Order => 100;

        public ValueTask<Result> HandleAsync(SampleRequest request,
            RequestHandlerDelegate<SampleRequest> next, CancellationToken cancellationToken = default)
        {
            log.Add("Hundred");
            return next(request, cancellationToken);
        }
    }

    private sealed class MiddleBehavior(List<string> log)
        : IRequestPipelineBehavior<SampleRequest>, IOrderedPipelineBehavior
    {
        public uint Order => IOrderedPipelineBehavior.Middle;

        public ValueTask<Result> HandleAsync(SampleRequest request,
            RequestHandlerDelegate<SampleRequest> next, CancellationToken cancellationToken = default)
        {
            log.Add("Middle");
            return next(request, cancellationToken);
        }
    }

    // No IOrderedPipelineBehavior — defaults to Last (innermost).
    private sealed class DefaultLastBehavior(List<string> log) : IRequestPipelineBehavior<SampleRequest>
    {
        public ValueTask<Result> HandleAsync(SampleRequest request,
            RequestHandlerDelegate<SampleRequest> next, CancellationToken cancellationToken = default)
        {
            log.Add("DefaultLast");
            return next(request, cancellationToken);
        }
    }
}
