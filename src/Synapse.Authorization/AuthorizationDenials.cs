using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

internal static class AuthorizationDenials
{
    public static IAuthorizationDenialRecorder[] ToArray(IEnumerable<IAuthorizationDenialRecorder> recorders)
    {
        ArgumentNullException.ThrowIfNull(recorders);
        return recorders as IAuthorizationDenialRecorder[] ?? recorders.ToArray();
    }

    public static async ValueTask<TResult> DenyAsync<TRequest, TResult>(IAuthorizationDenialRecorder[] recorders,
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
}
