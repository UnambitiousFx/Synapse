using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization.Tests;

[TestSubject(typeof(SynapseAuthorizationOptions))]
public sealed class DenialFailureTests
{
    private const string HiddenId = "hidden-1";

    public static TheoryData<string, AuthorizationDenialReason> Denials => new()
    {
        { "anonymous", AuthorizationDenialReason.Unauthenticated },
        { "role", AuthorizationDenialReason.MissingRole },
        { "scope", AuthorizationDenialReason.MissingScope },
        { "resource", AuthorizationDenialReason.ResourceAccessDenied }
    };

    [Theory]
    [MemberData(nameof(Denials))]
    public async Task InvokeAsync_WithDenialFailure_ReturnsTheApplicationsFailure(string scenario,
        AuthorizationDenialReason reason)
    {
        // Arrange (Given)
        AuthorizationDenial? seen = null;
        await using var provider = BuildProvider(o => o.DenialFailure = denial =>
        {
            seen = denial;
            return denial.Reason == AuthorizationDenialReason.Unauthenticated
                ? new UnauthenticatedFailure("Sign in first.") { Code = "core.sign_in" }
                : new UnauthorizedFailure($"Missing {denial.Requirement}.") { Code = "core.denied" };
        });

        // Act (When)
        var failure = await DenyAsync(provider, scenario);

        // Assert (Then) — the recorders still hear about it, with the denial the factory saw
        Assert.NotNull(seen);
        Assert.Equal(reason, seen.Reason);
        Assert.Same(seen, Assert.Single(provider.GetRequiredService<DenialLog>().Denials));
        if (reason == AuthorizationDenialReason.Unauthenticated)
        {
            Assert.IsType<UnauthenticatedFailure>(failure);
            Assert.Equal("core.sign_in", failure.Code);
            Assert.Equal("Sign in first.", failure.Message);
        }
        else
        {
            Assert.IsType<UnauthorizedFailure>(failure);
            Assert.Equal("core.denied", failure.Code);
            Assert.Equal($"Missing {seen.Requirement}.", failure.Message);
        }
    }

    [Theory]
    [MemberData(nameof(Denials))]
    public async Task InvokeAsync_WhenDenialFailureReturnsNull_ReturnsTheDefaultFailure(string scenario,
        AuthorizationDenialReason reason)
    {
        // Arrange (Given)
        await using var provider = BuildProvider(o => o.DenialFailure = _ => null);

        // Act (When)
        var failure = await DenyAsync(provider, scenario);

        // Assert (Then)
        Assert.Equal(reason == AuthorizationDenialReason.Unauthenticated
            ? FailureCodes.Unauthenticated
            : FailureCodes.Unauthorized, failure.Code);
    }

    [Theory]
    [MemberData(nameof(Denials))]
    public async Task InvokeAsync_WhenDenialFailureChangesTheType_Throws(string scenario,
        AuthorizationDenialReason reason)
    {
        // Arrange (Given) — a denial must not turn into a 404 (or a 401 into a 403)
        await using var provider = BuildProvider(o => o.DenialFailure = denial =>
            denial.Reason == AuthorizationDenialReason.Unauthenticated
                ? new UnauthorizedFailure("wrong kind")
                : new NotFoundFailure("Item", "42"));

        // Act (When)
        var act = () => DenyAsync(provider, scenario);

        // Assert (Then)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains(reason.ToString(), exception.Message);
        Assert.Contains(nameof(SynapseAuthorizationOptions.DenialFailure), exception.Message);
        Assert.Empty(provider.GetRequiredService<DenialLog>().Denials);
    }

    [Fact]
    public async Task InvokeAsync_WhenResourceHiddenAndDenialFailureReturnsNotFound_ReturnsTheApplicationsFailure()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(o => o.DenialFailure = denial =>
            new NotFoundFailure("Item", denial.ResourceId!) { Code = "core.item_not_found" });

        // Act (When)
        var failure = await DenyAsync(provider, "hidden");

        // Assert (Then)
        Assert.IsType<NotFoundFailure>(failure);
        Assert.Equal("core.item_not_found", failure.Code);
        Assert.Equal(AuthorizationDenialReason.ResourceHidden,
            Assert.Single(provider.GetRequiredService<DenialLog>().Denials).Reason);
    }

    [Fact]
    public async Task InvokeAsync_WhenResourceHiddenAndDenialFailureReturnsNull_ReturnsTheDefaultNotFound()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(o => o.DenialFailure = _ => null);

        // Act (When)
        var failure = await DenyAsync(provider, "hidden");

        // Assert (Then)
        Assert.IsType<NotFoundFailure>(failure);
        Assert.Equal(FailureCodes.NotFound, failure.Code);
    }

    [Fact]
    public async Task InvokeAsync_WhenResourceHiddenAndDenialFailureReturnsUnauthorized_Throws()
    {
        // Arrange (Given) — a hidden resource must not turn back into a 403 that discloses it
        await using var provider = BuildProvider(o => o.DenialFailure = _ => new UnauthorizedFailure("denied"));

        // Act (When)
        var act = () => DenyAsync(provider, "hidden");

        // Assert (Then)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains(nameof(AuthorizationDenialReason.ResourceHidden), exception.Message);
        Assert.Empty(provider.GetRequiredService<DenialLog>().Denials);
    }

    [Fact]
    public async Task InvokeStreamAsync_WithDenialFailure_YieldsTheApplicationsFailure()
    {
        // Arrange (Given)
        AuthorizationDenial? seen = null;
        await using var provider = BuildProvider(o => o.DenialFailure = denial =>
        {
            seen = denial;
            return new UnauthorizedFailure("Reserved to administrators.") { Code = "core.admin_required" };
        });

        // Act (When)
        var items = await StreamAsync(provider);

        // Assert (Then)
        Assert.True(Assert.Single(items).TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal("core.admin_required", failure.Code);
        Assert.NotNull(seen);
        Assert.Same(seen, Assert.Single(provider.GetRequiredService<DenialLog>().Denials));
    }

    [Fact]
    public async Task InvokeStreamAsync_WhenDenialFailureChangesTheType_ThrowsWithoutRecording()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(o => o.DenialFailure = _ => new NotFoundFailure("Item", "42"));

        // Act (When)
        var act = () => StreamAsync(provider);

        // Assert (Then)
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(provider.GetRequiredService<DenialLog>().Denials);
    }

    private static async Task<List<Result<int>>> StreamAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();
        var items = new List<Result<int>>();
        await foreach (var item in invoker.InvokeStreamAsync(new StreamAdminItems(),
                           TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private static async Task<Failure> DenyAsync(ServiceProvider provider, string scenario)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (scenario == "anonymous")
        {
            services.GetRequiredService<Caller>().Principal = Principals.Anonymous();
        }
        else if (scenario == "scope")
        {
            services.GetRequiredService<Caller>().Principal = Principals.Authenticated();
        }

        var invoker = services.GetRequiredService<IInvoker>();
        IResult result = scenario switch
        {
            "role" => await invoker.InvokeAsync(new PurgeItems(), TestContext.Current.CancellationToken),
            "resource" => await invoker.InvokeAsync(new RenameItem("42", "x"), TestContext.Current.CancellationToken),
            "hidden" => await invoker.InvokeAsync(new RenameItem(HiddenId, "x"),
                TestContext.Current.CancellationToken),
            _ => await invoker.InvokeAsync(new GetItem("42"), TestContext.Current.CancellationToken)
        };
        Assert.True(result.TryGetFailure(out var failure));
        return failure;
    }

    private static ServiceProvider BuildProvider(Action<SynapseAuthorizationOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<DenialLog>();
        services.AddScoped<Caller>();
        services.AddScoped(sp => sp.GetRequiredService<Caller>().Principal);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<GetItemHandler, GetItem, string>();
            cfg.RegisterRequestHandler<NoOpHandler<RenameItem>, RenameItem>();
            cfg.RegisterRequestHandler<NoOpHandler<PurgeItems>, PurgeItems>();
            cfg.RegisterStreamRequestHandler<EmptyStreamHandler, StreamAdminItems, int>();
        });
        services.AddSynapseAuthorization(o =>
        {
            o.Policy<DenyAll>();
            o.DenialRecorder<LoggingRecorder>();
            configure(o);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class DenialLog
    {
        public List<AuthorizationDenial> Denials { get; } = [];
    }

    private sealed class LoggingRecorder(DenialLog log) : IAuthorizationDenialRecorder
    {
        public ValueTask RecordAsync(AuthorizationDenial denial, CancellationToken cancellationToken = default)
        {
            log.Denials.Add(denial);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Caller
    {
        public ICurrentPrincipal Principal { get; set; } =
            Principals.Authenticated(scopes: ["read", "write"]);
    }

    private sealed class GetItemHandler : IRequestHandler<GetItem, string>
    {
        public ValueTask<Result<string>> HandleAsync(GetItem request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<string>>(Result.Success("item"));
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

    private sealed class EmptyStreamHandler : IStreamRequestHandler<StreamAdminItems, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(StreamAdminItems request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class DenyAll : IResourceAccessPolicy
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
            return new ValueTask<ResourceAccess>(request.ResourceId == HiddenId
                ? ResourceAccess.Hidden
                : ResourceAccess.Denied);
        }
    }
}
