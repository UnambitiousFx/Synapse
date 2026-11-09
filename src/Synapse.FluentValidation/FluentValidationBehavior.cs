using FluentValidation;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.FluentValidation;

/// <summary>
///     Runs every <see cref="IValidator{T}" /> registered for <typeparamref name="TRequest" /> and returns a
///     <see cref="ValidationFailure" /> instead of calling the handler when one of them fails.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public sealed class FluentValidationBehavior<TRequest> : IRequestPipelineBehavior<TRequest>, IOrderedPipelineBehavior
    where TRequest : IRequest
{
    private readonly IValidator<TRequest>[] _validators;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="validators">The validators registered for <typeparamref name="TRequest" />.</param>
    /// <param name="options">The behavior options.</param>
    public FluentValidationBehavior(IEnumerable<IValidator<TRequest>> validators,
        FluentValidationBehaviorOptions options)
    {
        _validators = validators.ToArray();
        Order = options.Order;
    }

    /// <inheritdoc />
    public uint Order { get; }

    /// <inheritdoc />
    public ValueTask<Result> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        return _validators.Length == 0
            ? next(request, cancellationToken)
            : ValidateThenHandleAsync(request, next, cancellationToken);
    }

    private async ValueTask<Result> ValidateThenHandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken)
    {
        var failure = await FluentValidationRunner.ValidateAsync(_validators, request, cancellationToken);
        return failure is null
            ? await next(request, cancellationToken)
            : Result.Failure(failure);
    }
}

/// <summary>
///     Runs every <see cref="IValidator{T}" /> registered for <typeparamref name="TRequest" /> and returns a
///     <see cref="ValidationFailure" /> instead of calling the handler when one of them fails.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class FluentValidationBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>,
    IOrderedPipelineBehavior
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly IValidator<TRequest>[] _validators;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="validators">The validators registered for <typeparamref name="TRequest" />.</param>
    /// <param name="options">The behavior options.</param>
    public FluentValidationBehavior(IEnumerable<IValidator<TRequest>> validators,
        FluentValidationBehaviorOptions options)
    {
        _validators = validators.ToArray();
        Order = options.Order;
    }

    /// <inheritdoc />
    public uint Order { get; }

    /// <inheritdoc />
    public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        return _validators.Length == 0
            ? next(request, cancellationToken)
            : ValidateThenHandleAsync(request, next, cancellationToken);
    }

    private async ValueTask<Result<TResponse>> ValidateThenHandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken)
    {
        var failure = await FluentValidationRunner.ValidateAsync(_validators, request, cancellationToken);
        return failure is null
            ? await next(request, cancellationToken)
            : Result.Failure<TResponse>(failure);
    }
}

internal static class FluentValidationRunner
{
    public static async ValueTask<ValidationFailure?> ValidateAsync<TRequest>(IValidator<TRequest>[] validators,
        TRequest request,
        CancellationToken cancellationToken)
    {
        List<ValidationError>? errors = null;
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (result.IsValid)
            {
                continue;
            }

            errors ??= [];
            foreach (var error in result.Errors)
            {
                // FluentValidation reports object-level rules with an empty property name; Functional uses null.
                errors.Add(new ValidationError(string.IsNullOrEmpty(error.PropertyName) ? null : error.PropertyName,
                    error.ErrorMessage, error.ErrorCode));
            }
        }

        return errors is null ? null : new ValidationFailure(errors);
    }
}
