using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Synapse.AspNetCore.Http;

/// <summary>
///     Writes the <c>WWW-Authenticate</c> challenge RFC 9110 §15.5.2 requires on every 401. A failure the pipeline maps
///     to 401 never goes through the authentication middleware, so nothing else would write it.
/// </summary>
internal static class AuthenticationChallenge
{
    // Used when the app registers no authentication: the scheme of a token-based API, the usual case for Synapse.
    private const string FallbackScheme = "Bearer";

    /// <summary>
    ///     Adds the challenge just before the response starts, only when the final status is 401 and no challenge was
    ///     already written. Checking the final status keeps it right whatever <c>IFailureHttpMapper</c> is registered.
    /// </summary>
    public static void Register(HttpContext context)
    {
        context.Response.OnStarting(static async state =>
        {
            var httpContext = (HttpContext)state;
            var response = httpContext.Response;
            if (response.StatusCode != StatusCodes.Status401Unauthorized
                || response.Headers.ContainsKey(HeaderNames.WWWAuthenticate))
            {
                return;
            }

            var schemes = httpContext.RequestServices.GetService<IAuthenticationSchemeProvider>();
            var scheme = schemes is null ? null : await schemes.GetDefaultChallengeSchemeAsync();
            response.Headers.WWWAuthenticate = scheme?.Name ?? FallbackScheme;
        }, context);
    }
}

/// <summary>A failure result that adds the RFC 9110 challenge when it resolves to 401.</summary>
internal sealed class ChallengingHttpResult : IHttpResult
{
    private readonly IHttpResult _inner;

    public ChallengingHttpResult(IHttpResult inner)
    {
        _inner = inner;
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        AuthenticationChallenge.Register(httpContext);
        return _inner.ExecuteAsync(httpContext);
    }
}

/// <summary>A failure action result that adds the RFC 9110 challenge when it resolves to 401.</summary>
internal sealed class ChallengingActionResult : IActionResult
{
    private readonly IActionResult _inner;

    public ChallengingActionResult(IActionResult inner)
    {
        _inner = inner;
    }

    public Task ExecuteResultAsync(ActionContext context)
    {
        AuthenticationChallenge.Register(context.HttpContext);
        return _inner.ExecuteResultAsync(context);
    }
}
