using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Features.Tasks.Validators;

/// <summary>
///     Validates <see cref="CreateTaskCommand" /> before it reaches the handler.
///     The <c>[Validator]</c> attribute makes the source generator auto-register both this validator and the
///     closed <c>RequestValidationBehavior&lt;CreateTaskCommand, CreateTaskResult&gt;</c> that runs it — no
///     manual <c>AddValidator</c> or pipeline-behavior registration is needed.
/// </summary>
[Validator]
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