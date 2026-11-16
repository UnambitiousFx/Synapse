using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization.Tests;

public sealed record StreamItems(int Count) : IStreamRequest<int>;

public sealed record StreamAdminItems : IStreamRequest<int>, IRequiresRole
{
    public string Role => "admin";
}

public sealed record StreamExport : IStreamRequest<int>, IRequiresScope
{
    public string Scope => "reports:export";
}

public sealed record StreamHousehold(string HouseholdId) : IStreamRequest<int>, IResourceScoped
{
    public string ResourceId => HouseholdId;
    public string Access => "read";
}

public sealed record StreamAccounts(string? AccountId) : IStreamRequest<int>, IOptionallyResourceScoped
{
    public string? OptionalResourceId => AccountId;
    public string Access => "read";
}

public sealed record StreamInvoices(string CustomerId) : IStreamRequest<int>;

public sealed record StreamPublicFeed : IStreamRequest<int>, IAllowAnonymous;

public sealed record StreamPublicAdmin : IStreamRequest<int>, IAllowAnonymous, IRequiresRole
{
    public string Role => "admin";
}

public sealed record StreamPublicScoped : IStreamRequest<int>, IAllowAnonymous, IRequiresScope
{
    public string Scope => "feed:read";
}

public sealed record StreamPublicHousehold(string HouseholdId) : IStreamRequest<int>, IAllowAnonymous, IResourceScoped
{
    public string ResourceId => HouseholdId;
    public string Access => "read";
}

[TestSubject(typeof(StreamAuthorizationBehavior<,>))]
public sealed class StreamAuthorizationTests
{
    [Fact]
    public async Task InvokeStreamAsync_WithAuthorizedCaller_YieldsEveryItem()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]));

        // Act (When)
        var items = await CollectAsync(provider, new StreamItems(3));

        // Assert (Then)
        Assert.Equal([0, 1, 2], items.Select(r => r.Match(v => v, _ => -1)));
        Assert.Empty(provider.GetRequiredService<StreamDenialLog>().Denials);
    }

    [Fact]
    public async Task InvokeStreamAsync_WithAnonymousCaller_YieldsOneUnauthenticatedFailureWithoutRunningHandler()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Anonymous());

        // Act (When)
        var items = await CollectAsync(provider, new StreamItems(3));

        // Assert (Then)
        var item = Assert.Single(items);
        Assert.True(item.TryGetFailure(out var failure));
        Assert.IsType<UnauthenticatedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.Unauthenticated, denial.Reason);
        Assert.Equal(0, provider.GetRequiredService<StreamHandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeStreamAsync_WithoutQueryScope_TreatsStreamAsQuery()
    {
        // Arrange (Given) — the caller only has the command scope
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["write"]));

        // Act (When)
        var items = await CollectAsync(provider, new StreamItems(3));

        // Assert (Then)
        var item = Assert.Single(items);
        Assert.True(item.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.MissingScope, denial.Reason);
        Assert.Equal(SynapseAuthorizationOptions.DefaultQueryScope, denial.Requirement);
        Assert.Equal(0, provider.GetRequiredService<StreamHandlerCalls>().Count);
    }

    [Theory]
    [InlineData(new[] { "read" }, false)]
    [InlineData(new[] { "reports:export" }, true)]
    public async Task InvokeStreamAsync_WithDeclaredScope_ChecksItInsteadOfDefault(string[] scopes, bool allowed)
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: scopes));

        // Act (When)
        var items = await CollectAsync(provider, new StreamExport());

        // Assert (Then)
        Assert.Equal(allowed, items.All(r => r.IsSuccess));
        Assert.Equal(allowed ? 1 : 0, provider.GetRequiredService<StreamHandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeStreamAsync_WithoutRequiredRole_ReportsMissingRole()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]));

        // Act (When)
        var items = await CollectAsync(provider, new StreamAdminItems());

        // Assert (Then)
        Assert.Single(items);
        var denial = Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.MissingRole, denial.Reason);
        Assert.Equal(0, provider.GetRequiredService<StreamHandlerCalls>().Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task InvokeStreamAsync_ResourceScopedWithoutAllowingPolicy_IsDenied(bool? policyAllows)
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]), policyAllows);

        // Act (When)
        var items = await CollectAsync(provider, new StreamHousehold("h-1"));

        // Assert (Then)
        var item = Assert.Single(items);
        Assert.True(item.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.ResourceAccessDenied, denial.Reason);
        Assert.Equal("h-1", denial.ResourceId);
        Assert.Equal(0, provider.GetRequiredService<StreamHandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeStreamAsync_ResourceScopedWithHidingPolicy_YieldsNotFoundFailure()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]), hidePolicy: true);

        // Act (When)
        var items = await CollectAsync(provider, new StreamHousehold("h-1"));

        // Assert (Then)
        Assert.True(Assert.Single(items).TryGetFailure(out var failure));
        Assert.IsType<NotFoundFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.ResourceHidden, denial.Reason);
        Assert.Equal("h-1", denial.ResourceId);
        Assert.Equal(0, provider.GetRequiredService<StreamHandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeStreamAsync_ResourceScopedWithAllowingPolicy_YieldsItems()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]), policyAllows: true);

        // Act (When)
        var items = await CollectAsync(provider, new StreamHousehold("h-1"));

        // Assert (Then)
        Assert.True(Assert.Single(items).IsSuccess);
    }

    [Fact]
    public async Task InvokeStreamAsync_OptionallyScopedWithoutResource_YieldsItemsWithoutPolicy()
    {
        // Arrange (Given) — no policy registered
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]));

        // Act (When)
        var items = await CollectAsync(provider, new StreamAccounts(null));

        // Assert (Then)
        Assert.True(Assert.Single(items).IsSuccess);
        Assert.Empty(provider.GetRequiredService<StreamDenialLog>().Denials);
    }

    [Fact]
    public async Task InvokeStreamAsync_OptionallyScopedWithResource_AsksPolicy()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]), policyAllows: false);

        // Act (When)
        var items = await CollectAsync(provider, new StreamAccounts("acc-1"));

        // Assert (Then)
        Assert.True(Assert.Single(items).TryGetFailure(out _));
        Assert.Equal("acc-1", Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials).ResourceId);
    }

    [Theory]
    [InlineData("billable", true)]
    [InlineData("other", false)]
    public async Task InvokeStreamAsync_WithRequestPolicy_AsksIt(string customerId, bool allowed)
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Authenticated(scopes: ["read"]));

        // Act (When)
        var items = await CollectAsync(provider, new StreamInvoices(customerId));

        // Assert (Then)
        Assert.Equal(allowed, Assert.Single(items).IsSuccess);
        Assert.Equal(allowed ? 0 : 1, provider.GetRequiredService<StreamDenialLog>().Denials.Count);
        if (!allowed)
        {
            Assert.Equal(AuthorizationDenialReason.RequestDenied,
                provider.GetRequiredService<StreamDenialLog>().Denials[0].Reason);
        }
    }

    [Fact]
    public async Task InvokeStreamAsync_AllowAnonymousFromAnonymousCaller_YieldsItems()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(Principals.Anonymous());

        // Act (When)
        var items = await CollectAsync(provider, new StreamPublicFeed());

        // Assert (Then)
        Assert.True(Assert.Single(items).IsSuccess);
        Assert.Empty(provider.GetRequiredService<StreamDenialLog>().Denials);
    }

    public static TheoryData<IStreamRequest<int>, AuthorizationDenialReason> PublicStreamsWithRequirements => new()
    {
        { new StreamPublicAdmin(), AuthorizationDenialReason.MissingRole },
        { new StreamPublicScoped(), AuthorizationDenialReason.MissingScope },
        { new StreamPublicHousehold("h-1"), AuthorizationDenialReason.ResourceAccessDenied }
    };

    [Theory]
    [MemberData(nameof(PublicStreamsWithRequirements))]
    public async Task InvokeStreamAsync_AllowAnonymousWithDeclaredRequirement_StillChecksIt(
        IStreamRequest<int> request,
        AuthorizationDenialReason reason)
    {
        // Arrange (Given) — IAllowAnonymous only skips the actor check, not the requirements the stream declares
        await using var provider = BuildProvider(Principals.Anonymous(), policyAllows: false);

        // Act (When)
        var items = await CollectAsync(provider, request);

        // Assert (Then)
        var item = Assert.Single(items);
        Assert.True(item.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials);
        Assert.Equal(reason, denial.Reason);
        Assert.Equal(0, provider.GetRequiredService<StreamHandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeStreamAsync_FailingActorAndRole_ReportsTheActorFirst()
    {
        // Arrange (Given) — anonymous and without the role: the checks run actor first, like the request pipeline
        await using var provider = BuildProvider(Principals.Anonymous());

        // Act (When)
        var items = await CollectAsync(provider, new StreamAdminItems());

        // Assert (Then)
        Assert.True(Assert.Single(items).TryGetFailure(out var failure));
        Assert.IsType<UnauthenticatedFailure>(failure);
        var denial = Assert.Single(provider.GetRequiredService<StreamDenialLog>().Denials);
        Assert.Equal(AuthorizationDenialReason.Unauthenticated, denial.Reason);
    }

    private static async Task<List<Result<int>>> CollectAsync(ServiceProvider provider, IStreamRequest<int> request)
    {
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();
        var items = new List<Result<int>>();
        await foreach (var item in invoker.InvokeStreamAsync(request, TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private static ServiceProvider BuildProvider(ICurrentPrincipal principal,
        bool? policyAllows = null,
        bool hidePolicy = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<StreamHandlerCalls>();
        services.AddSingleton<StreamDenialLog>();
        services.AddSingleton(principal);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamItems>, StreamItems, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamAdminItems>, StreamAdminItems, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamExport>, StreamExport, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamHousehold>, StreamHousehold, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamAccounts>, StreamAccounts, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamInvoices>, StreamInvoices, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamPublicFeed>, StreamPublicFeed, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamPublicAdmin>, StreamPublicAdmin, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamPublicScoped>, StreamPublicScoped, int>();
            cfg.RegisterStreamRequestHandler<CountingHandler<StreamPublicHousehold>, StreamPublicHousehold, int>();
        });
        services.AddSynapseAuthorization(o =>
        {
            o.DenialRecorder<LoggingRecorder>();
            o.RequestPolicy<StreamInvoices, BillableInvoicesPolicy>();
            if (hidePolicy)
            {
                o.Policy<FixedPolicy.Hide>();
            }

            if (policyAllows is { } allows)
            {
                if (allows)
                {
                    o.Policy<FixedPolicy.Allow>();
                }
                else
                {
                    o.Policy<FixedPolicy.Deny>();
                }
            }
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class CountingHandler<TRequest>(StreamHandlerCalls calls) : IStreamRequestHandler<TRequest, int>
        where TRequest : IStreamRequest<int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(TRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            calls.Count++;
            var count = request is StreamItems items ? items.Count : 1;
            for (var i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return Result.Success(i);
            }
        }
    }

    private sealed class StreamHandlerCalls
    {
        public int Count { get; set; }
    }

    private sealed class StreamDenialLog
    {
        public List<AuthorizationDenial> Denials { get; } = [];
    }

    private sealed class LoggingRecorder(StreamDenialLog log) : IAuthorizationDenialRecorder
    {
        public ValueTask RecordAsync(AuthorizationDenial denial, CancellationToken cancellationToken = default)
        {
            log.Denials.Add(denial);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BillableInvoicesPolicy : IRequestAccessPolicy<StreamInvoices>
    {
        public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
            StreamInvoices request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(request.CustomerId == "billable");
        }
    }

    private static class FixedPolicy
    {
        public sealed class Allow : IResourceAccessPolicy
        {
            public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
                IResourceScoped request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(true);
            }
        }

        public sealed class Deny : IResourceAccessPolicy
        {
            public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
                IResourceScoped request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(false);
            }
        }

        public sealed class Hide : IResourceAccessPolicy
        {
            public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
                IResourceScoped request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(false);
            }

            public ValueTask<ResourceAccess> CheckAsync(ICurrentPrincipal principal,
                IResourceScoped request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ResourceAccess>(ResourceAccess.Hidden);
            }
        }
    }
}
