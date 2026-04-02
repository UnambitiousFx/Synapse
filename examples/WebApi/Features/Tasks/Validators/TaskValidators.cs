using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.WebApi.Features.Tasks.Validators;

/// <summary>
///     Validates <see cref="CreateTaskCommand" /> before it reaches the handler.
///     Registered via <c>cfg.AddValidator&lt;CreateTaskCommandValidator, CreateTaskCommand, Guid&gt;()</c>
///     and activated by <c>RequestValidationBehavior&lt;CreateTaskCommand, Guid&gt;</c> in the pipeline.
/// </summary>
public sealed class CreateTaskCommandValidator : IRequestValidator<CreateTaskCommand>
{
    /// <inheritdoc />
    public ValueTask<Result> ValidateAsync(CreateTaskCommand request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return ValueTask.FromResult(Result.Failure("Title is required."));
        }

        if (request.Title.Length > 200)
        {
            return ValueTask.FromResult(Result.Failure("Title must not exceed 200 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return ValueTask.FromResult(Result.Failure("Description is required."));
        }

        if (request.Description.Length > 1000)
        {
            return ValueTask.FromResult(Result.Failure("Description must not exceed 1000 characters."));
        }

        return ValueTask.FromResult(Result.Success());
    }
}