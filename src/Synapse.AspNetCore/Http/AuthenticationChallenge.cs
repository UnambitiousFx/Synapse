using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Synapse.AspNetCore.Http;

/// <summary>
///     Writes the <c>WWW-Authenticate</c> challenge RFC 9110 §15.5.2 requires on every 401. A failure the pipeline maps
///     to 401 never goes through the authentication middleware, so nothing else would write it.
/// </summary>
internal static class AuthenticationChallenge
{
    /// <summary>
    ///     Adds the challenge just before the response starts, only when the final status is 401 and no challenge was
    ///     already written. Checking the final status keeps it right whatever <c>IFailureHttpMapper</c> is registered.
    /// </summary>
    public static void Register(HttpContext context, string? challenge)
    {
        if (string.IsNullOrEmpty(challenge))
        {
            return;
        }

        context.Response.OnStarting(() =>
        {
            var response = context.Response;
            if (response.StatusCode == StatusCodes.Status401Unauthorized
                && !response.Headers.ContainsKey(HeaderNames.WWWAuthenticate))
            {
                response.Headers.WWWAuthenticate = challenge;
            }

            return Task.CompletedTask;
        });
    }
}

/// <summary>A failure result that adds the RFC 9110 challenge when it resolves to 401.</summary>
internal sealed class ChallengingHttpResult : IHttpResult
{
    private readonly string? _challenge;
    private readonly IHttpResult _inner;

    public ChallengingHttpResult(IHttpResult inner, string? challenge)
    {
        _inner = inner;
        _challenge = challenge;
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        AuthenticationChallenge.Register(httpContext, _challenge);
        return _inner.ExecuteAsync(httpContext);
    }
}

/// <summary>A failure action result that adds the RFC 9110 challenge when it resolves to 401.</summary>
internal sealed class ChallengingActionResult : IActionResult
{
    private readonly string? _challenge;
    private readonly IActionResult _inner;

    public ChallengingActionResult(IActionResult inner, string? challenge)
    {
        _inner = inner;
        _challenge = challenge;
    }

    public Task ExecuteResultAsync(ActionContext context)
    {
        AuthenticationChallenge.Register(context.HttpContext, _challenge);
        return _inner.ExecuteResultAsync(context);
    }
}
