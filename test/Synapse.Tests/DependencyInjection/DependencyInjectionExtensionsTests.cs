using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.DependencyInjection;

[TestSubject(typeof(DependencyInjectionExtensions))]
public sealed class DependencyInjectionExtensionsTests
{
    [Fact]
    public async Task GivenRequest_WhenResolve_ThenReturnResult()
    {
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>();
            })
            .AddLogging()
            .BuildServiceProvider();

        var handler = services.GetRequiredService<IRequestHandler<RequestWithResponseExample, int>>();
        services.GetRequiredService<IContextFactory>()
            .Create();

        var result = await handler.HandleAsync(new RequestWithResponseExample(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenRequestWithBehavior_WhenResolve_ThenReturnResult()
    {
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>();
                cfg.RegisterRequestPipelineBehavior<TestRequestPipelineBehavior>();
            })
            .AddLogging()
            .BuildServiceProvider();

        var handler = services.GetRequiredService<IRequestHandler<RequestWithResponseExample, int>>();
        services.GetRequiredService<IContextFactory>()
            .Create();

        var result = await handler.HandleAsync(new RequestWithResponseExample(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}