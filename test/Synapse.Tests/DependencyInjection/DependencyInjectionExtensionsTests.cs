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
            .Create(PropagatedContext.None);

        var result = await handler.HandleAsync(new RequestWithResponseExample(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenRequestWithBehavior_WhenResolve_ThenReturnResult()
    {
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>();
                cfg.RegisterRequestPipelineBehavior<TestRequestPipelineBehavior<RequestWithResponseExample, int>,
                    RequestWithResponseExample, int>();
            })
            .AddLogging()
            .BuildServiceProvider();

        var handler = services.GetRequiredService<IRequestHandler<RequestWithResponseExample, int>>();
        services.GetRequiredService<IContextFactory>()
            .Create(PropagatedContext.None);

        var result = await handler.HandleAsync(new RequestWithResponseExample(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenConditionalRequestHandler_WhenConditionIsTrue_ThenHandlerIsRegistered()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandlerWhen<RequestWithResponseExampleHandler, RequestWithResponseExample,
                    int>(() => true);
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act
        var handler = services.GetService<IRequestHandler<RequestWithResponseExample, int>>();

        // Assert
        Assert.NotNull(handler);
        services.GetRequiredService<IContextFactory>()
            .Create(PropagatedContext.None);
        var result = await handler.HandleAsync(new RequestWithResponseExample(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GivenConditionalRequestHandler_WhenConditionIsFalse_ThenHandlerIsNotRegistered()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandlerWhen<RequestWithResponseExampleHandler, RequestWithResponseExample,
                    int>(() => false);
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act
        var handler = services.GetService<IRequestHandler<RequestWithResponseExample, int>>();

        // Assert
        Assert.Null(handler);
    }

    [Fact]
    public async Task GivenConditionalEventHandler_WhenConditionIsTrue_ThenHandlerIsRegistered()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddSynapse(cfg => { cfg.RegisterEventHandlerWhen<EventExampleHandler1, EventExample>(() => true); })
            .AddLogging()
            .BuildServiceProvider();

        // Act
        var handler = services.GetService<IEventHandler<EventExample>>();

        // Assert
        Assert.NotNull(handler);
    }

    [Fact]
    public void GivenConditionalEventHandler_WhenConditionIsFalse_ThenHandlerIsNotRegistered()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddSynapse(cfg => { cfg.RegisterEventHandlerWhen<EventExampleHandler1, EventExample>(() => false); })
            .AddLogging()
            .BuildServiceProvider();

        // Act
        var handler = services.GetService<IEventHandler<EventExample>>();

        // Assert
        Assert.Null(handler);
    }

    [Fact]
    public async Task GivenConditionalRequestHandler_WhenConditionIsTrue_ThenInvokerCanDispatch()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandlerWhen<RequestWithResponseExampleHandler, RequestWithResponseExample,
                    int>(() => true);
            })
            .AddLogging()
            .BuildServiceProvider();

        var invoker = services.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new RequestWithResponseExample(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenConditionalRequestHandler_WhenConditionIsFalse_ThenInvokerThrowsNoHandlerException()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandlerWhen<RequestWithResponseExampleHandler, RequestWithResponseExample,
                    int>(() => false);
            })
            .AddLogging()
            .BuildServiceProvider();

        var invoker = services.GetRequiredService<IInvoker>();

        // Act & Assert (When & Then)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync(new RequestWithResponseExample(), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void GivenConditionalRegistration_WhenBasedOnEnvironmentVariable_ThenRegistersCorrectly()
    {
        // Arrange
        Environment.SetEnvironmentVariable("FEATURE_EXPERIMENTAL_HANDLER", "true");

        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandlerWhen<RequestWithResponseExampleHandler, RequestWithResponseExample,
                    int>(() => Environment.GetEnvironmentVariable("FEATURE_EXPERIMENTAL_HANDLER") == "true");
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act
        var handler = services.GetService<IRequestHandler<RequestWithResponseExample, int>>();

        // Assert
        Assert.NotNull(handler);

        // Cleanup
        Environment.SetEnvironmentVariable("FEATURE_EXPERIMENTAL_HANDLER", null);
    }
}