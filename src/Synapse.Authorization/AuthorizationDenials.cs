using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

internal static class AuthorizationDenials
{
    public static async ValueTask<TResult> DenyAsync<TRequest, TResult>(
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        RequestNext<TRequest, TResult> next,
        Denied denied,
        Func<AuthorizationDenial, Failure?>? denialFailure,
        CancellationToken cancellationToken)
        where TResult : IResult
    {
        var failure = FailureFor(denied, denialFailure);
        await RecordAsync(recorders, denied.Denial, cancellationToken);
        return next.Fail(failure);
    }

    /// <summary>
    ///     The application's failure for a denial (<see cref="SynapseAuthorizationOptions.DenialFailure" />), or the
    ///     default one. The type must not change: it is what transports map to 401 / 403 and what MCP hides existence
    ///     behind, so a factory returning another kind of failure is a bug, not a choice.
    /// </summary>
    public static Failure FailureFor(Denied denied, Func<AuthorizationDenial, Failure?>? denialFailure)
    {
        if (denialFailure?.Invoke(denied.Denial) is not { } failure)
        {
            return denied.Failure;
        }

        var expected = denied.Failure.GetType();
        if (!expected.IsInstanceOfType(failure))
        {
            throw new InvalidOperationException(
                $"{nameof(SynapseAuthorizationOptions)}.{nameof(SynapseAuthorizationOptions.DenialFailure)} returned " +
                $"a {failure.GetType().Name} for a {denied.Denial.Reason} denial. Return an {expected.Name} (with " +
                "your own code and message), or null for the default failure.");
        }

        return failure;
    }

    // The recorders are only enumerated on the denial path, so the allowed path costs nothing extra.
    public static async ValueTask RecordAsync(IEnumerable<IAuthorizationDenialRecorder> recorders,
        AuthorizationDenial denial,
        CancellationToken cancellationToken)
    {
        foreach (var recorder in recorders)
        {
            await recorder.RecordAsync(denial, cancellationToken);
        }
    }

    /// <summary>
    ///     Rejects a requirement no caller could meet. Throwing rather than denying makes the broken request type show
    ///     up in logs and tests instead of looking like an ordinary 401/403.
    /// </summary>
    public static string RequireValue<TRequest>(string? value, string marker, string property)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{typeof(TRequest).FullName} implements {marker} but its {property} is null, empty or whitespace. " +
                $"Return the {property.ToLowerInvariant()} the request requires.");
        }

        return value;
    }
}
