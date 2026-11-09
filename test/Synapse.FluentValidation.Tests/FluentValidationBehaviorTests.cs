using FluentValidation;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.FluentValidation.Tests;

[TestSubject(typeof(FluentValidationBehavior<>))]
public sealed class FluentValidationBehaviorTests
{
    [Fact]
    public async Task InvokeAsync_WithInvalidRequest_ReturnsValidationFailureAndSkipsHandler()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(services =>
            services.AddScoped<IValidator<CreateUser>, CreateUserValidator>());
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new CreateUser(""), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        var validation = Assert.IsType<ValidationFailure>(failure);
        var error = Assert.Single(validation.Errors);
        Assert.Equal(nameof(CreateUser.Name), error.Field);
        Assert.Equal("NAME_REQUIRED", error.Code);
        Assert.Equal(0, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidRequestWithResponse_ReturnsValidationFailure()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(services =>
            services.AddScoped<IValidator<GetUser>, GetUserValidator>());
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new GetUser(0), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        var error = Assert.Single(Assert.IsType<ValidationFailure>(failure).Errors);
        Assert.Equal(nameof(GetUser.Id), error.Field);
        Assert.Equal(0, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeAsync_WithValidRequest_CallsHandler()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(services =>
            services.AddScoped<IValidator<GetUser>, GetUserValidator>());
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new GetUser(42), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("user-42", value);
        Assert.Equal(1, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeAsync_WithoutValidator_CallsHandler()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(_ => { });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new CreateUser(""), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, provider.GetRequiredService<HandlerCalls>().Count);
    }

    [Fact]
    public async Task InvokeAsync_WithSeveralFailingValidators_ReturnsEveryErrorInOneFailure()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(services =>
        {
            services.AddScoped<IValidator<CreateUser>, CreateUserValidator>();
            services.AddScoped<IValidator<CreateUser>, ObjectLevelValidator>();
        });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new CreateUser(""), TestContext.Current.CancellationToken);

        // Assert (Then) — the object-level rule has no field
        Assert.True(result.TryGetFailure(out var failure));
        var errors = Assert.IsType<ValidationFailure>(failure).Errors;
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Field == nameof(CreateUser.Name));
        Assert.Contains(errors, e => e.Field is null && e.Message == "Rejected");
    }

    [Fact]
    public void AddSynapseFluentValidation_WithOrder_AppliesItToTheBehavior()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapseFluentValidation(o => o.Order = IOrderedPipelineBehavior.First);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act (When)
        var behavior = scope.ServiceProvider.GetServices<IRequestPipelineBehavior<CreateUser>>().Single();

        // Assert (Then)
        Assert.Equal(IOrderedPipelineBehavior.First, Assert.IsType<FluentValidationBehavior<CreateUser>>(behavior).Order);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> registerValidators)
    {
        var services = new ServiceCollection();
        services.AddSingleton<HandlerCalls>();
        registerValidators(services);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<CreateUserHandler, CreateUser>();
            cfg.RegisterRequestHandler<GetUserHandler, GetUser, string>();
        });
        services.AddSynapseFluentValidation();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public sealed record CreateUser(string Name) : IRequest;

    public sealed record GetUser(int Id) : IRequest<string>;

    private sealed class HandlerCalls
    {
        public int Count { get; set; }
    }

    private sealed class CreateUserHandler(HandlerCalls calls) : IRequestHandler<CreateUser>
    {
        public ValueTask<Result> HandleAsync(CreateUser request, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class GetUserHandler(HandlerCalls calls) : IRequestHandler<GetUser, string>
    {
        public ValueTask<Result<string>> HandleAsync(GetUser request, CancellationToken cancellationToken = default)
        {
            calls.Count++;
            return new ValueTask<Result<string>>(Result.Success($"user-{request.Id}"));
        }
    }

    private sealed class CreateUserValidator : AbstractValidator<CreateUser>
    {
        public CreateUserValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithErrorCode("NAME_REQUIRED");
        }
    }

    private sealed class ObjectLevelValidator : AbstractValidator<CreateUser>
    {
        public ObjectLevelValidator()
        {
            RuleFor(x => x).Must(_ => false).WithMessage("Rejected").OverridePropertyName("");
        }
    }

    private sealed class GetUserValidator : AbstractValidator<GetUser>
    {
        public GetUserValidator()
        {
            RuleFor(x => x.Id).GreaterThan(0);
        }
    }
}
