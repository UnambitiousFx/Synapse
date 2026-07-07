using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

public sealed class RequestValidationBehaviorTests
{
    // ── RequestValidationBehavior<TRequest, TResponse> (with response) ───────

    [Fact]
    public async Task HandleAsync_WhenAllValidatorsSucceed_InvokesNext()
    {
        // Arrange (Given)
        var validators = new IRequestValidator<ValidatedRequest>[]
        {
            new SuccessValidator(),
            new SuccessValidator()
        };

        var behavior = new RequestValidationBehavior<ValidatedRequest, int>(validators);
        var nextCalled = false;

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedRequest(), (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success(42));
        }, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WhenValidationFails_DoesNotInvokeNext()
    {
        // Arrange (Given)
        var validators = new IRequestValidator<ValidatedRequest>[]
        {
            new SuccessValidator(),
            new FailureValidator()
        };

        var behavior = new RequestValidationBehavior<ValidatedRequest, int>(validators);
        var nextCalled = false;

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedRequest(), (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success(42));
        }, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(nextCalled);
        Assert.False(result.IsSuccess);
    }

    // ── RequestValidationBehavior<TRequest> (void / no-response) ────────────

    [Fact]
    public async Task HandleAsync_VoidRequest_WhenAllValidatorsSucceed_InvokesNext()
    {
        // Arrange (Given)
        var validators = new IRequestValidator<ValidatedVoidRequest>[]
        {
            new VoidSuccessValidator(),
            new VoidSuccessValidator()
        };
        var behavior = new RequestValidationBehavior<ValidatedVoidRequest>(validators);
        var nextCalled = false;

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedVoidRequest(), (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        }, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_VoidRequest_WhenValidationFails_DoesNotInvokeNext()
    {
        // Arrange (Given)
        var validators = new IRequestValidator<ValidatedVoidRequest>[]
        {
            new VoidSuccessValidator(),
            new VoidFailureValidator()
        };
        var behavior = new RequestValidationBehavior<ValidatedVoidRequest>(validators);
        var nextCalled = false;

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedVoidRequest(), (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        }, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(nextCalled);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_VoidRequest_WithEmptyValidators_InvokesNext()
    {
        // Arrange (Given)
        var behavior = new RequestValidationBehavior<ValidatedVoidRequest>(
            Array.Empty<IRequestValidator<ValidatedVoidRequest>>());
        var nextCalled = false;

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedVoidRequest(), (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        }, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Order_IsLast_ForVoidBehavior()
    {
        // Arrange (Given)
        var behavior = new RequestValidationBehavior<ValidatedVoidRequest>(
            Array.Empty<IRequestValidator<ValidatedVoidRequest>>());

        // Assert (Then)
        Assert.Equal(IOrderedPipelineBehavior.Last, behavior.Order);
    }

    [Fact]
    public void Order_IsLast_ForResponseBehavior()
    {
        // Arrange (Given)
        var behavior = new RequestValidationBehavior<ValidatedRequest, int>(
            Array.Empty<IRequestValidator<ValidatedRequest>>());

        // Assert (Then)
        Assert.Equal(IOrderedPipelineBehavior.Last, behavior.Order);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private sealed record ValidatedRequest : IRequest<int>;

    private sealed class SuccessValidator : IRequestValidator<ValidatedRequest>
    {
        public ValueTask<Result> ValidateAsync(ValidatedRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class FailureValidator : IRequestValidator<ValidatedRequest>
    {
        public ValueTask<Result> ValidateAsync(ValidatedRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Failure("validation failed"));
        }
    }

    private sealed record ValidatedVoidRequest : IRequest;

    private sealed class VoidSuccessValidator : IRequestValidator<ValidatedVoidRequest>
    {
        public ValueTask<Result> ValidateAsync(ValidatedVoidRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class VoidFailureValidator : IRequestValidator<ValidatedVoidRequest>
    {
        public ValueTask<Result> ValidateAsync(ValidatedVoidRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Failure("void validation failed"));
        }
    }
}
