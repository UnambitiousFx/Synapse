using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization.Tests;

public sealed class MultiShapeTests
{
    private sealed record DualQuery : IQuery<string>, IStreamRequest<int>;

    private sealed record MixedTypedFirst : ICommand<string>, IStreamRequest<int>;

    private sealed record MixedStreamFirst : ICommand<string>, IStreamRequest<int>;

    private sealed record PreRegisteredOptionsQuery : IQuery<string>;

    private sealed record CommandAndQuery : ICommand<string>, IQuery<string>;

    [Fact]
    public async Task Invoke_RequestWithTypedAndStreamHandlers_RejectsAnonymousCallerOnBothPaths()
    {
        // Arrange (Given)
        await using var provider = Build(Principals.Anonymous(), services => services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedHandler<DualQuery>, DualQuery, string>();
            cfg.RegisterStreamRequestHandler<StreamHandler<DualQuery>, DualQuery, int>();
        }));

        // Act (When)
        var typed = await InvokeAsync(provider, new DualQuery());
        var stream = await CollectAsync(provider, new DualQuery());

        // Assert (Then)
        Assert.True(typed.TryGetFailure(out var typedFailure));
        Assert.IsType<UnauthenticatedFailure>(typedFailure);
        Assert.True(Assert.Single(stream).TryGetFailure(out var streamFailure));
        Assert.IsType<UnauthenticatedFailure>(streamFailure);
    }

    [Fact]
    public async Task Invoke_CommandTypedShapeRegisteredFirst_NeedsCommandScopeOnBothPaths()
    {
        // Arrange (Given)
        await using var provider = Build(Principals.Authenticated(scopes: ["read"]), services => services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedHandler<MixedTypedFirst>, MixedTypedFirst, string>();
            cfg.RegisterStreamRequestHandler<StreamHandler<MixedTypedFirst>, MixedTypedFirst, int>();
        }));

        // Act (When)
        var typed = await InvokeAsync(provider, new MixedTypedFirst());
        var stream = await CollectAsync(provider, new MixedTypedFirst());

        // Assert (Then)
        Assert.True(typed.TryGetFailure(out var typedFailure));
        Assert.IsType<UnauthorizedFailure>(typedFailure);
        Assert.True(Assert.Single(stream).TryGetFailure(out var streamFailure));
        Assert.IsType<UnauthorizedFailure>(streamFailure);
    }

    [Fact]
    public async Task Invoke_CommandTypedShapeRegisteredLast_NeedsCommandScopeOnBothPaths()
    {
        // Arrange (Given)
        await using var provider = Build(Principals.Authenticated(scopes: ["read"]), services => services.AddSynapse(cfg =>
        {
            cfg.RegisterStreamRequestHandler<StreamHandler<MixedStreamFirst>, MixedStreamFirst, int>();
            cfg.RegisterRequestHandler<TypedHandler<MixedStreamFirst>, MixedStreamFirst, string>();
        }));

        // Act (When)
        var typed = await InvokeAsync(provider, new MixedStreamFirst());
        var stream = await CollectAsync(provider, new MixedStreamFirst());

        // Assert (Then)
        Assert.True(typed.TryGetFailure(out var typedFailure));
        Assert.IsType<UnauthorizedFailure>(typedFailure);
        Assert.True(Assert.Single(stream).TryGetFailure(out var streamFailure));
        Assert.IsType<UnauthorizedFailure>(streamFailure);
    }

    [Theory]
    [InlineData("read", false)]
    [InlineData("write", true)]
    public async Task Invoke_RequestImplementingCommandAndQuery_NeedsTheCommandScope(string scope, bool allowed)
    {
        // Arrange (Given)
        await using var provider = Build(Principals.Authenticated(scopes: [scope]), services => services.AddSynapse(cfg =>
            cfg.RegisterRequestHandler<TypedHandler<CommandAndQuery>, CommandAndQuery, string>()));

        // Act (When)
        var result = await InvokeAsync(provider, new CommandAndQuery());

        // Assert (Then)
        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed)
        {
            Assert.True(result.TryGetFailure(out var failure));
            Assert.IsType<UnauthorizedFailure>(failure);
        }
    }

    [Fact]
    public async Task AddSynapseAuthorization_OptionsPreRegisteredByTheApplication_StillRejectsAnonymousCaller()
    {
        // Arrange (Given)
        await using var provider = Build(Principals.Anonymous(), services =>
        {
            services.AddSingleton(new SynapseAuthorizationOptions());
            services.AddSynapse(cfg =>
                cfg.RegisterRequestHandler<TypedHandler<PreRegisteredOptionsQuery>, PreRegisteredOptionsQuery, string>());
        });

        // Act (When)
        var result = await InvokeAsync(provider, new PreRegisteredOptionsQuery());

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthenticatedFailure>(failure);
    }

    private static ServiceProvider Build(ICurrentPrincipal principal, Action<ServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton(principal);
        register(services);
        services.AddSynapseAuthorization(o =>
        {
            o.QueryScope = "read";
            o.CommandScope = "write";
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<Result<string>> InvokeAsync<TRequest>(ServiceProvider provider, TRequest request)
        where TRequest : IRequest<string>
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<List<Result<int>>> CollectAsync(ServiceProvider provider, IStreamRequest<int> request)
    {
        await using var scope = provider.CreateAsyncScope();
        var items = new List<Result<int>>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<IInvoker>()
                           .InvokeStreamAsync(request, TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed class TypedHandler<TRequest> : IRequestHandler<TRequest, string>
        where TRequest : IRequest<string>
    {
        public ValueTask<Result<string>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<string>>(Result.Success("ok"));
        }
    }

    private sealed class StreamHandler<TRequest> : IStreamRequestHandler<TRequest, int>
        where TRequest : IStreamRequest<int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(TRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Result.Success(1);
        }
    }
}
