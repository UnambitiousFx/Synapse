using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using NSubstitute;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.ModelContextProtocol.Tests;

[TestSubject(typeof(ServiceCollectionExtensions))]
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSynapseMcp_RegistersInvokerPolicyAndOptions()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IInvoker>());

        // Act (When)
        services.AddSynapseMcp(o => o.MaxAnswerLength = 1000);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        // Assert (Then)
        Assert.IsType<McpInvoker>(scope.ServiceProvider.GetRequiredService<IMcpInvoker>());
        Assert.IsType<DefaultMcpFailurePolicy>(provider.GetRequiredService<IMcpFailurePolicy>());
        var options = provider.GetRequiredService<IOptions<SynapseMcpOptions>>().Value;
        Assert.Equal(1000, options.MaxAnswerLength);
        Assert.True(options.HideExistence);
    }

    [Fact]
    public void AddSynapseMcp_WithCustomPolicyRegisteredFirst_KeepsIt()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSingleton<IMcpFailurePolicy, CustomPolicy>();

        // Act (When)
        services.AddSynapseMcp();
        using var provider = services.BuildServiceProvider();

        // Assert (Then)
        Assert.IsType<CustomPolicy>(provider.GetRequiredService<IMcpFailurePolicy>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxAnswerLength_WhenNotPositive_Throws(int value)
    {
        // Arrange (Given)
        var options = new SynapseMcpOptions();

        // Act (When)
        var exception = Record.Exception(() => options.MaxAnswerLength = value);

        // Assert (Then)
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    private sealed class CustomPolicy : IMcpFailurePolicy
    {
        public CallToolResult ToToolError(Failure failure)
        {
            return new CallToolResult { IsError = true };
        }
    }
}
