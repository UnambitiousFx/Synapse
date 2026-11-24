using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
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

    [Fact]
    public async Task HandleAsync_WhenOneValidatorFails_ReturnsThatFailureUnchanged()
    {
        // Arrange (Given)
        var validationFailure = new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]);
        var validators = new IRequestValidator<ValidatedRequest>[]
        {
            new SuccessValidator(),
            new SingleFailureValidator(validationFailure)
        };
        var behavior = new RequestValidationBehavior<ValidatedRequest, int>(validators);

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedRequest(), (_, _) =>
            ValueTask.FromResult(Result.Success(42)), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.Same(validationFailure, failure);
    }

    [Fact]
    public async Task HandleAsync_WhenMultipleValidatorsFailWithValidationFailures_MergesTheirErrors()
    {
        // Arrange (Given)
        var first = new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]);
        var second = new ValidationFailure([new ValidationError("quantity", "Quantity must be positive.", "orders.quantity_invalid")]);
        var validators = new IRequestValidator<ValidatedRequest>[]
        {
            new SingleFailureValidator(first),
            new SingleFailureValidator(second)
        };
        var behavior = new RequestValidationBehavior<ValidatedRequest, int>(validators);

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedRequest(), (_, _) =>
            ValueTask.FromResult(Result.Success(42)), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        var merged = Assert.IsType<ValidationFailure>(failure);
        Assert.Equal(["name", "quantity"], merged.Errors.Select(e => e.Field));
    }

    [Fact]
    public async Task HandleAsync_WhenMultipleValidationFailuresHaveExtraKeyCollision_LaterValidatorWins()
    {
        // Arrange (Given)
        var first = new ValidationFailure(
            [new ValidationError("name", "Name is required.", "orders.name_required")],
            new Dictionary<string, object?> { ["requestId"] = "first", ["step"] = 1 });
        var second = new ValidationFailure(
            [new ValidationError("quantity", "Quantity must be positive.", "orders.quantity_invalid")],
            new Dictionary<string, object?> { ["requestId"] = "second" });
        var validators = new IRequestValidator<ValidatedRequest>[]
        {
            new SingleFailureValidator(first),
            new SingleFailureValidator(second)
        };
        var behavior = new RequestValidationBehavior<ValidatedRequest, int>(validators);

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedRequest(), (_, _) =>
            ValueTask.FromResult(Result.Success(42)), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        var merged = Assert.IsType<ValidationFailure>(failure);
        Assert.NotNull(merged.Extra);
        Assert.Equal("second", merged.Extra!["requestId"]);
        Assert.Equal(1, merged.Extra["step"]);
    }

    [Fact]
    public async Task HandleAsync_WhenMultipleValidatorsFailWithDifferentFailureTypes_ReturnsAggregateFailure()
    {
        // Arrange (Given)
        var first = new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]);
        var second = new Failure("orders.not_found", "Order not found.");
        var validators = new IRequestValidator<ValidatedRequest>[]
        {
            new SingleFailureValidator(first),
            new SingleFailureValidator(second)
        };
        var behavior = new RequestValidationBehavior<ValidatedRequest, int>(validators);

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedRequest(), (_, _) =>
            ValueTask.FromResult(Result.Success(42)), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        var aggregate = Assert.IsType<AggregateFailure>(failure);
        Assert.Equal([first, second], aggregate.Errors);
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
    public async Task HandleAsync_VoidRequest_WhenOneValidatorFails_ReturnsThatFailureUnchanged()
    {
        // Arrange (Given)
        var validationFailure = new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]);
        var validators = new IRequestValidator<ValidatedVoidRequest>[]
        {
            new VoidSuccessValidator(),
            new VoidSingleFailureValidator(validationFailure)
        };
        var behavior = new RequestValidationBehavior<ValidatedVoidRequest>(validators);

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedVoidRequest(), (_, _) =>
            ValueTask.FromResult(Result.Success()), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.Same(validationFailure, failure);
    }

    [Fact]
    public async Task HandleAsync_VoidRequest_WhenMultipleValidatorsFailWithValidationFailures_MergesTheirErrors()
    {
        // Arrange (Given)
        var first = new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]);
        var second = new ValidationFailure([new ValidationError("quantity", "Quantity must be positive.", "orders.quantity_invalid")]);
        var validators = new IRequestValidator<ValidatedVoidRequest>[]
        {
            new VoidSingleFailureValidator(first),
            new VoidSingleFailureValidator(second)
        };
        var behavior = new RequestValidationBehavior<ValidatedVoidRequest>(validators);

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedVoidRequest(), (_, _) =>
            ValueTask.FromResult(Result.Success()), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        var merged = Assert.IsType<ValidationFailure>(failure);
        Assert.Equal(["name", "quantity"], merged.Errors.Select(e => e.Field));
    }

    [Fact]
    public async Task HandleAsync_VoidRequest_WhenMultipleValidatorsFailWithDifferentFailureTypes_ReturnsAggregateFailure()
    {
        // Arrange (Given)
        var first = new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]);
        var second = new Failure("orders.not_found", "Order not found.");
        var validators = new IRequestValidator<ValidatedVoidRequest>[]
        {
            new VoidSingleFailureValidator(first),
            new VoidSingleFailureValidator(second)
        };
        var behavior = new RequestValidationBehavior<ValidatedVoidRequest>(validators);

        // Act (When)
        var result = await behavior.HandleAsync(new ValidatedVoidRequest(), (_, _) =>
            ValueTask.FromResult(Result.Success()), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        var aggregate = Assert.IsType<AggregateFailure>(failure);
        Assert.Equal([first, second], aggregate.Errors);
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

    private sealed class SingleFailureValidator : IRequestValidator<ValidatedRequest>
    {
        private readonly Failure _failure;

        public SingleFailureValidator(Failure failure)
        {
            _failure = failure;
        }

        public ValueTask<Result> ValidateAsync(ValidatedRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Failure(_failure));
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

    private sealed class VoidSingleFailureValidator : IRequestValidator<ValidatedVoidRequest>
    {
        private readonly Failure _failure;

        public VoidSingleFailureValidator(Failure failure)
        {
            _failure = failure;
        }

        public ValueTask<Result> ValidateAsync(ValidatedVoidRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Failure(_failure));
        }
    }
}
