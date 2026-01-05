using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a contract for validating requests of a specified type.
/// </summary>
/// <typeparam name="TRequest">The type of the request to be validated.</typeparam>
public interface IRequestValidator<in TRequest>
{
    /// Validates the specified request asynchronously.
    /// <param name="request">The request to be validated.</param>
    /// <param name="cancellationToken">A token to cancel the operation if needed.</param>
    /// <return>A task representing the result of the validation, containing information about success or failure.</return>
    ValueTask<Result> ValidateAsync(TRequest request, CancellationToken cancellationToken = default);
}