using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

internal static class AuthorizationDenials
{
    public static async ValueTask<TResult> DenyAsync<TRequest, TResult>(
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        RequestNext<TRequest, TResult> next,
        Denied denied,
        CancellationToken cancellationToken)
        where TResult : IResult
    {
        await RecordAsync(recorders, denied.Denial, cancellationToken);
        return next.Fail(denied.Failure);
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
