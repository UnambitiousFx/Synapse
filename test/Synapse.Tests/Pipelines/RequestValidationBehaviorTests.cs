using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

public sealed class RequestValidationBehaviorTests
{
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
        var result = await behavior.HandleAsync(new ValidatedRequest(), () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success(42));
        }, CancellationToken.None);

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
        var result = await behavior.HandleAsync(new ValidatedRequest(), () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success(42));
        }, CancellationToken.None);

        // Assert (Then)
        Assert.False(nextCalled);
        Assert.False(result.IsSuccess);
    }

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
}
