namespace UnambitiousFx.Synapse.AspNetCore;

/// <summary>
///     Options for <see cref="Http.IHttpInvoker" /> and <see cref="Mvc.IMvcInvoker" />.
/// </summary>
public sealed class SynapseAspNetCoreOptions
{
    /// <summary>
    ///     The default value of <see cref="AuthenticationChallenge" />.
    /// </summary>
    public const string DefaultAuthenticationChallenge = "Bearer";

    /// <summary>
    ///     The <c>WWW-Authenticate</c> value written on a failure that resolves to <c>401 Unauthorized</c>, which
    ///     RFC 9110 §15.5.2 requires. Defaults to <c>Bearer</c>.
    /// </summary>
    /// <remarks>
    ///     This is the HTTP authentication method the client must use (<c>Bearer</c>, <c>Basic realm="api"</c>,
    ///     <c>Negotiate</c>…), not the name an authentication handler is registered under. Set it to
    ///     <see langword="null" /> to write no challenge, for instance in a cookie-based app that never answers 401.
    ///     A challenge already written by a custom failure mapper is kept.
    /// </remarks>
    public string? AuthenticationChallenge { get; set; } = DefaultAuthenticationChallenge;
}
