using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization.Tests;

[TestSubject(typeof(SystemPrincipal))]
public sealed class SystemPrincipalTests
{
    [Fact]
    public async Task InvokeAsync_SystemPrincipalByDefault_PassesEveryCheckWithoutAskingPolicies()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(SystemPrincipal.Instance, out var resourcePolicy,
            out var requestPolicy);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();
        var ct = TestContext.Current.CancellationToken;

        // Act (When)
        var results = new IResult[]
        {
            await invoker.InvokeAsync(new GetItem("42"), ct),
            await invoker.InvokeAsync(new PurgeItems(), ct),
            await invoker.InvokeAsync(new ExportReport(), ct),
            await invoker.InvokeAsync(new RenameItem("42", "x"), ct),
            await invoker.InvokeAsync(new CreateInvoice("other"), ct)
        };

        // Assert (Then)
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Empty(provider.GetRequiredService<Denials>().List);
        await resourcePolicy.DidNotReceiveWithAnyArgs().IsAllowedAsync(null!, null!, CancellationToken.None);
        await requestPolicy.DidNotReceiveWithAnyArgs().IsAllowedAsync(null!, null!, CancellationToken.None);
    }

    [Fact]
    public async Task InvokeAsync_WithResourceChecksBypass_ChecksRoleButSkipsPolicies()
    {
        // Arrange (Given) — a system identity with only the write scope
        var principal = Principals.Authenticated("nightly-job", ["read", "write"]);
        principal.IsSystem.Returns(true);
        await using var provider = BuildProvider(principal, out var resourcePolicy, out var requestPolicy,
            o => o.SystemBypass = SystemBypass.ResourceChecks);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();
        var ct = TestContext.Current.CancellationToken;

        // Act (When)
        var purge = await invoker.InvokeAsync(new PurgeItems(), ct);
        var rename = await invoker.InvokeAsync(new RenameItem("42", "x"), ct);
        var invoice = await invoker.InvokeAsync(new CreateInvoice("other"), ct);

        // Assert (Then)
        Assert.True(purge.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.True(rename.IsSuccess);
        Assert.True(invoice.IsSuccess);
        var denial = Assert.Single(provider.GetRequiredService<Denials>().List);
        Assert.Equal(AuthorizationDenialReason.MissingRole, denial.Reason);
        await resourcePolicy.DidNotReceiveWithAnyArgs().IsAllowedAsync(null!, null!, CancellationToken.None);
        await requestPolicy.DidNotReceiveWithAnyArgs().IsAllowedAsync(null!, null!, CancellationToken.None);
    }

    [Fact]
    public async Task InvokeAsync_WithoutBypass_ChecksTheSystemLikeAnyCaller()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(SystemPrincipal.Instance, out var resourcePolicy, out _,
            o => o.SystemBypass = SystemBypass.None);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new RenameItem("42", "x"), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
        var denial = Assert.Single(provider.GetRequiredService<Denials>().List);
        Assert.Equal(AuthorizationDenialReason.ResourceAccessDenied, denial.Reason);
        Assert.Equal(SystemPrincipal.Instance.Id, denial.PrincipalId);
        await resourcePolicy.ReceivedWithAnyArgs(1).IsAllowedAsync(null!, null!, CancellationToken.None);
    }

    [Fact]
    public async Task InvokeStreamAsync_SystemPrincipal_YieldsItemsWithoutAskingPolicy()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(SystemPrincipal.Instance, out var resourcePolicy, out _);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var items = new List<Result<int>>();
        await foreach (var item in invoker.InvokeStreamAsync(new StreamHousehold("h-1"),
                           TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        // Assert (Then)
        Assert.True(Assert.Single(items).IsSuccess);
        await resourcePolicy.DidNotReceiveWithAnyArgs().IsAllowedAsync(null!, null!, CancellationToken.None);
    }

    [Fact]
    public void Instance_IsAnAuthenticatedSystemPrincipal()
    {
        // Arrange (Given)
        ICurrentPrincipal principal = SystemPrincipal.Instance;

        // Act (When)
        var isSystem = principal.IsSystem;

        // Assert (Then)
        Assert.True(isSystem);
        Assert.True(principal.IsAuthenticated);
        Assert.Equal("system", principal.Id);
    }

    [Fact]
    public void IsSystem_ByDefault_IsFalse()
    {
        // Arrange (Given)
        ICurrentPrincipal principal = new ClaimsCurrentPrincipal(null);

        // Act (When)
        var isSystem = principal.IsSystem;

        // Assert (Then)
        Assert.False(isSystem);
    }

    [Fact]
    public void AddSynapseAuthorization_WithUndefinedSystemBypass_Throws()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        var act = () => services.AddSynapseAuthorization(o => o.SystemBypass = (SystemBypass)42);

        // Assert (Then)
        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal(nameof(SynapseAuthorizationOptions.SystemBypass), exception.ParamName);
    }

    private static ServiceProvider BuildProvider(ICurrentPrincipal principal,
        out IResourceAccessPolicy resourcePolicy,
        out IRequestAccessPolicy<CreateInvoice> requestPolicy,
        Action<SynapseAuthorizationOptions>? configure = null)
    {
        resourcePolicy = Substitute.For<IResourceAccessPolicy>();
        requestPolicy = Substitute.For<IRequestAccessPolicy<CreateInvoice>>();
        var services = new ServiceCollection();
        services.AddSingleton<Denials>();
        services.AddSingleton(principal);
        services.AddSingleton(resourcePolicy);
        services.AddSingleton(requestPolicy);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<ItemHandler, GetItem, string>();
            cfg.RegisterRequestHandler<ExportHandler, ExportReport, string>();
            cfg.RegisterRequestHandler<NoOpHandler<PurgeItems>, PurgeItems>();
            cfg.RegisterRequestHandler<NoOpHandler<RenameItem>, RenameItem>();
            cfg.RegisterRequestHandler<NoOpHandler<CreateInvoice>, CreateInvoice>();
            cfg.RegisterStreamRequestHandler<OneItemHandler, StreamHousehold, int>();
        });
        services.AddSynapseAuthorization(o =>
        {
            o.DenialRecorder<Recorder>();
            configure?.Invoke(o);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class Denials
    {
        public List<AuthorizationDenial> List { get; } = [];
    }

    private sealed class Recorder(Denials denials) : IAuthorizationDenialRecorder
    {
        public ValueTask RecordAsync(AuthorizationDenial denial, CancellationToken cancellationToken = default)
        {
            denials.List.Add(denial);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ItemHandler : IRequestHandler<GetItem, string>
    {
        public ValueTask<Result<string>> HandleAsync(GetItem request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<string>>(Result.Success("item"));
        }
    }

    private sealed class ExportHandler : IRequestHandler<ExportReport, string>
    {
        public ValueTask<Result<string>> HandleAsync(ExportReport request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<string>>(Result.Success("report"));
        }
    }

    private sealed class NoOpHandler<TRequest> : IRequestHandler<TRequest>
        where TRequest : IRequest
    {
        public ValueTask<Result> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class OneItemHandler : IStreamRequestHandler<StreamHousehold, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(StreamHousehold request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Result.Success(1);
        }
    }
}
