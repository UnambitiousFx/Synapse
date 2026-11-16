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
    public void Describe_ListsTheBehaviorsInActorRoleScopeRequestResourceOrderInAuthorizationStage()
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
            typeof(ActorAuthorizationBehavior<RenameItem, Result>),
            typeof(RoleAuthorizationBehavior<RenameItem, Result>),
            typeof(ScopeAuthorizationBehavior<RenameItem, Result>),
            typeof(RequestPolicyAuthorizationBehavior<RenameItem, Result>),
            typeof(ResourceAuthorizationBehavior<RenameItem, Result>)
        ], authorization.Select(b => b.Type));
        Assert.All(authorization, b => Assert.Equal(PipelineStage.Authorization, b.Stage));
    }

    [Fact]
    public void Describe_AppBehaviorsPlacedByStage_WrapOrFollowAuthorization()
    {
        // Arrange (Given) — no numbers: one behavior by stage only, one placed after the last check
        var services = CreateServices(Principals.Authenticated(), cfg =>
        {
            cfg.AddOpenGenericResultPipelineBehavior(typeof(TracingBehavior<,>));
            cfg.ConfigureBehaviorOrdering(typeof(TracingBehavior<,>), PipelineStage.Observability);
            cfg.AddOpenGenericResultPipelineBehavior(typeof(AuditBehavior<,>));
            cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior<,>), PipelineStage.Authorization,
                after: typeof(ResourceAuthorizationBehavior<,>));
        });
        services.AddSynapseAuthorization();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var describer = scope.ServiceProvider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var description = describer.Describe<RenameItem>();

        // Assert (Then)
        Assert.NotNull(description);
        var types = description.Behaviors.Select(b => b.Type.GetGenericTypeDefinition()).ToList();
        Assert.True(types.IndexOf(typeof(TracingBehavior<,>)) < types.IndexOf(typeof(ActorAuthorizationBehavior<,>)));
        Assert.Equal(
        [
            typeof(ActorAuthorizationBehavior<,>), typeof(RoleAuthorizationBehavior<,>),
            typeof(ScopeAuthorizationBehavior<,>), typeof(RequestPolicyAuthorizationBehavior<,>),
            typeof(ResourceAuthorizationBehavior<,>), typeof(AuditBehavior<,>)
        ], types.Skip(types.IndexOf(typeof(ActorAuthorizationBehavior<,>))).Take(6));
    }

    [Fact]
    public async Task InvokeAsync_AllowAnonymousRequestFromAnonymousCaller_RunsHandler()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Anonymous());
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new SignIn("a@b.c"), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, provider.GetRequiredService<HandlerCalls>().Count);
        Assert.Empty(provider.GetRequiredService<DenialLog>().Denials);
    }

    [Fact]
    public async Task AddSynapseAuthorization_WhenALaterCallSetsAStricterScope_EnforcesIt()
    {
        // Arrange (Given) — a shared module registers first, then the host tightens the query scope
        var services = CreateServices(Principals.Authenticated(scopes: ["read"]));
        services.AddSynapseAuthorization(o => o.DenialRecorder<LoggingDenialRecorder>());
        services.AddSynapseAuthorization(o => o.QueryScope = "loans:read");
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new GetItem("42"), TestContext.Current.CancellationToken);

        // Assert (Then) — and the first call's recorder still applies
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<DenialLog>().Denials);
        Assert.Equal("loans:read", denial.Requirement);
        Assert.Equal(0, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public void AddSynapseAuthorization_WhenCalledTwice_SharesOneOptionsInstance()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapseAuthorization(o => o.CommandScope = "loans:write");
        services.AddSynapseAuthorization(o => o.QueryScope = "loans:read");

        // Assert (Then) — both calls' settings kept
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(SynapseAuthorizationOptions));
        var options = Assert.IsType<SynapseAuthorizationOptions>(descriptor.ImplementationInstance);
        Assert.Equal("loans:read", options.QueryScope);
        Assert.Equal("loans:write", options.CommandScope);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddSynapseAuthorization_WithBlankDefaultScope_Throws(string scope)
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddSynapseAuthorization(o => o.QueryScope = scope));

        // Assert (Then)
        Assert.Equal(nameof(SynapseAuthorizationOptions.QueryScope), exception.ParamName);
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
        Assert.Equal(5, services.Count(d => d.ServiceType == typeof(IResultPipelineBehavior<,>)));
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

    [Fact]
    public async Task InvokeAsync_WithRequestPolicy_DeniesAfterTheScopeCheck()
    {
        // Arrange (Given)
        var services = CreateServices(Principals.Authenticated(scopes: ["write"]),
            cfg => cfg.RegisterRequestHandler<CreateInvoiceHandler, CreateInvoice>());
        services.AddSynapseAuthorization(o =>
        {
            o.DenialRecorder<LoggingDenialRecorder>();
            o.RequestPolicy<CreateInvoice, BillableCustomersPolicy>();
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var allowed = await invoker.InvokeAsync(new CreateInvoice("billable"), TestContext.Current.CancellationToken);
        var denied = await invoker.InvokeAsync(new CreateInvoice("other"), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(allowed.IsSuccess);
        Assert.True(denied.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<DenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.RequestDenied, denial.Reason);
        Assert.Equal(1, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeAsync_WithRequestPolicyButWithoutScope_ReportsTheScope()
    {
        // Arrange (Given)
        var services = CreateServices(Principals.Authenticated(),
            cfg => cfg.RegisterRequestHandler<CreateInvoiceHandler, CreateInvoice>());
        services.AddSynapseAuthorization(o =>
        {
            o.DenialRecorder<LoggingDenialRecorder>();
            o.RequestPolicy<CreateInvoice, BillableCustomersPolicy>();
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        await invoker.InvokeAsync(new CreateInvoice("other"), TestContext.Current.CancellationToken);

        // Assert (Then)
        var denial = Assert.Single(provider.GetRequiredService<DenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.MissingScope, denial.Reason);
    }

    [Fact]
    public void AddSynapseAuthorization_WithRequestPolicy_ReplacesThePreviousOne()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddScoped<IRequestAccessPolicy<CreateInvoice>>(_ => new BillableCustomersPolicy());

        // Act (When)
        services.AddSynapseAuthorization(o => o.RequestPolicy<CreateInvoice, BillableCustomersPolicy>());

        // Assert (Then)
        var policy = Assert.Single(services, d => d.ServiceType == typeof(IRequestAccessPolicy<CreateInvoice>));
        Assert.Equal(typeof(BillableCustomersPolicy), policy.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, policy.Lifetime);
    }

    private sealed class CreateInvoiceHandler(HandlerCalls calls) : IRequestHandler<CreateInvoice>
    {
        public ValueTask<Result> HandleAsync(CreateInvoice request, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return new ValueTask<Result>(Result.Success());
        }
    }

    private static ServiceProvider BuildProvider(ICurrentPrincipal principal,
        bool? policyAllows = null,
        IResourceAccessPolicy? policy = null,
        Action<SynapseAuthorizationOptions>? configure = null)
    {
        var services = CreateServices(principal);
        if (policy is not null)
        {
            services.AddSingleton(policy);
        }

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

    private static ServiceCollection CreateServices(ICurrentPrincipal principal,
        Action<ISynapseConfig>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<HandlerCalls>();
        services.AddSingleton<DenialLog>();
        services.AddSingleton(principal);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<GetItemHandler, GetItem, string>();
            cfg.RegisterRequestHandler<RenameItemHandler, RenameItem>();
            cfg.RegisterRequestHandler<PurgeItemsHandler, PurgeItems>();
            cfg.RegisterRequestHandler<SignInHandler, SignIn>();
            configure?.Invoke(cfg);
        });
        return services;
    }

    private sealed class SignInHandler(HandlerCalls calls) : IRequestHandler<SignIn>
    {
        public ValueTask<Result> HandleAsync(SignIn request, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return new ValueTask<Result>(Result.Success());
        }
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

    private sealed class TracingBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>
        where TResult : IResult
    {
        public ValueTask<TResult> HandleAsync(TRequest request,
            RequestNext<TRequest, TResult> next,
            CancellationToken cancellationToken = default)
        {
            return next.InvokeAsync(request, cancellationToken);
        }
    }

    private sealed class AuditBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>
        where TResult : IResult
    {
        public ValueTask<TResult> HandleAsync(TRequest request,
            RequestNext<TRequest, TResult> next,
            CancellationToken cancellationToken = default)
        {
            return next.InvokeAsync(request, cancellationToken);
        }
    }
}
