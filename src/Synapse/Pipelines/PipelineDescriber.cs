using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Default <see cref="IPipelineDescriber" />: opens a scope per call, reads the pipeline from what the
///     container resolves, and disposes the scope.
/// </summary>
internal sealed class PipelineDescriber : IPipelineDescriber
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PipelineDescriber(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public PipelineDescription? Describe<TRequest>()
        where TRequest : IRequest
    {
        using var scope = _scopeFactory.CreateScope();
        return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest>>());
    }

    public PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>());
    }

    private static PipelineDescription? DescribeHandler(object? handler)
    {
        return handler switch
        {
            null => null,
            IPipelineInfo info => new PipelineDescription([info.HandlerType], info.Behaviors),
            // Registered straight into the container rather than through Synapse: nothing wraps it, so no
            // behavior can apply.
            _ => new PipelineDescription([handler.GetType()], [])
        };
    }
}
