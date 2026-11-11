using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization.Tests;

[TestSubject(typeof(ServiceCollectionExtensions))]
public sealed class AuthorizationPipelineTests
{
    [Fact]
    public async Task InvokeAsync_WithAuthorizedCaller_RunsHandler()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read", "write"]),
            policyAllows: true);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var query = await invoker.InvokeAsync(new GetItem("42"), TestContext.Current.CancellationToken);
        var command = await invoker.InvokeAsync(new RenameItem("42", "renamed"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(query.TryGetValue(out var value));
        Assert.Equal("item-42", value);
        Assert.True(command.IsSuccess);
        Assert.Equal(2, provider.GetRequiredService<HandlerCalls>().Count);
        Assert.Empty(provider.GetRequiredService<DenialLog>().Denials);
    }

    [Fact]
    public async Task InvokeAsync_WithAnonymousCaller_ShortCircuitsBeforeScopeAndResourceChecks()
    {
        // Arrange (Given) — the request would also fail the scope and the resource checks
        var policy = Substitute.For<IResourceAccessPolicy>();
        await using var provider = BuildProvider(Principals.Anonymous(), policy: policy);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new RenameItem("42", "renamed"), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthenticatedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<DenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.Unauthenticated, denial.Reason);
        await policy.DidNotReceiveWithAnyArgs().IsAllowedAsync(null!, null!, CancellationToken.None);
        Assert.Equal(0, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeAsync_WithoutRoleOrScope_ReportsTheRoleFirst()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated());
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new PurgeItems(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<DenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.MissingRole, denial.Reason);
    }

    [Fact]
    public async Task InvokeAsync_WithScopeButDeniedResource_ReportsTheResource()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["write"]), policyAllows: false);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new RenameItem("42", "renamed"), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<DenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.ResourceAccessDenied, denial.Reason);
        Assert.Equal("42", denial.ResourceId);
        Assert.Equal(0, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeAsync_WithConfiguredScopes_UsesThem()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["items:read"]),
            configure: o => o.QueryScope = "items:read");
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new GetItem("42"), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Describe_ListsTheBehaviorsInActorRoleScopeResourceOrder()
    {
        // Arrange (Given)
        using var provider = BuildProvider(Principals.Authenticated());
        using var scope = provider.CreateScope();
        var describer = scope.ServiceProvider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var description = describer.Describe<RenameItem>();

        // Assert (Then)
        Assert.NotNull(description);
        var authorization = description.Behaviors
            .Where(b => b.Type.Namespace == typeof(ActorAuthorizationBehavior<,>).Namespace)
            .ToArray();
        Assert.Equal(
        [
            new BehaviorDescription(typeof(ActorAuthorizationBehavior<RenameItem, Result>),
                AuthorizationBehaviorOrder.Actor),
            new BehaviorDescription(typeof(RoleAuthorizationBehavior<RenameItem, Result>),
                AuthorizationBehaviorOrder.Role),
            new BehaviorDescription(typeof(ScopeAuthorizationBehavior<RenameItem, Result>),
                AuthorizationBehaviorOrder.Scope),
            new BehaviorDescription(typeof(ResourceAuthorizationBehavior<RenameItem, Result>),
                AuthorizationBehaviorOrder.Resource)
        ], authorization);
    }

    [Fact]
    public void AddSynapseAuthorization_WhenCalledTwice_RegistersEachBehaviorOnce()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapseAuthorization();
        services.AddSynapseAuthorization();

        // Assert (Then)
        Assert.Equal(4, services.Count(d => d.ServiceType == typeof(IResultPipelineBehavior<,>)));
    }

    [Fact]
    public void AddSynapseAuthorization_WithPolicy_ReplacesThePreviousPolicy()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddScoped<IResourceAccessPolicy, AllowAllPolicy>();

        // Act (When)
        services.AddSynapseAuthorization(o => o.Policy<DenyAllPolicy>());

        // Assert (Then)
        var policy = Assert.Single(services, d => d.ServiceType == typeof(IResourceAccessPolicy));
        Assert.Equal(typeof(DenyAllPolicy), policy.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, policy.Lifetime);
    }

    private static ServiceProvider BuildProvider(ICurrentPrincipal principal,
        bool? policyAllows = null,
        IResourceAccessPolicy? policy = null,
        Action<SynapseAuthorizationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<HandlerCalls>();
        services.AddSingleton<DenialLog>();
        services.AddSingleton(principal);
        if (policy is not null)
        {
            services.AddSingleton(policy);
        }

        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<GetItemHandler, GetItem, string>();
            cfg.RegisterRequestHandler<RenameItemHandler, RenameItem>();
            cfg.RegisterRequestHandler<PurgeItemsHandler, PurgeItems>();
        });
        services.AddSynapseAuthorization(o =>
        {
            o.DenialRecorder<LoggingDenialRecorder>();
            if (policyAllows == true)
            {
                o.Policy<AllowAllPolicy>();
            }
            else if (policyAllows == false)
            {
                o.Policy<DenyAllPolicy>();
            }

            configure?.Invoke(o);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class HandlerCalls
    {
        public int Count { get; set; }
    }

    private sealed class DenialLog
    {
        public List<AuthorizationDenial> Denials { get; } = [];
    }

    private sealed class LoggingDenialRecorder(DenialLog log) : IAuthorizationDenialRecorder
    {
        public ValueTask RecordAsync(AuthorizationDenial denial, CancellationToken cancellationToken = default)
        {
            log.Denials.Add(denial);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AllowAllPolicy : IResourceAccessPolicy
    {
        public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
            IResourceScoped request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(true);
        }
    }

    private sealed class DenyAllPolicy : IResourceAccessPolicy
    {
        public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
            IResourceScoped request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(false);
        }
    }

    private sealed class GetItemHandler(HandlerCalls calls) : IRequestHandler<GetItem, string>
    {
        public ValueTask<Result<string>> HandleAsync(GetItem request, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return new ValueTask<Result<string>>(Result.Success($"item-{request.Id}"));
        }
    }

    private sealed class RenameItemHandler(HandlerCalls calls) : IRequestHandler<RenameItem>
    {
        public ValueTask<Result> HandleAsync(RenameItem request, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class PurgeItemsHandler(HandlerCalls calls) : IRequestHandler<PurgeItems>
    {
        public ValueTask<Result> HandleAsync(PurgeItems request, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return new ValueTask<Result>(Result.Success());
        }
    }
}
