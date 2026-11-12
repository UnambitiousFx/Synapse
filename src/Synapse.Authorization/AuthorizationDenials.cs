using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

internal static class AuthorizationDenials
{
    // The recorders are only enumerated on the denial path, so the allowed path costs nothing extra.
    public static async ValueTask<TResult> DenyAsync<TRequest, TResult>(
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        RequestNext<TRequest, TResult> next,
        AuthorizationDenial denial,
        Failure failure,
        CancellationToken cancellationToken)
        where TResult : IResult
    {
        foreach (var recorder in recorders)
        {
            await recorder.RecordAsync(denial, cancellationToken);
        }

        return next.Fail(failure);
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
