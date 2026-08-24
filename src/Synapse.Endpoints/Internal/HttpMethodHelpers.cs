namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>Shared helpers used when declaring OpenAPI metadata from an endpoint's HTTP methods.</summary>
internal static class HttpMethodHelpers
{
    /// <summary>
    ///     Determines whether <em>every</em> verb in <paramref name="httpMethods" /> conventionally
    ///     carries no request body, so declaring <c>Accepts</c> for the endpoint would misrepresent it.
    /// </summary>
    /// <param name="httpMethods">The endpoint's declared HTTP methods.</param>
    /// <returns><see langword="true" /> when no declared verb carries a body.</returns>
    /// <remarks>
    ///     <para>
    ///         The bodyless set is <c>GET</c>/<c>DELETE</c>/<c>HEAD</c>/<c>OPTIONS</c>/<c>TRACE</c>:
    ///         none of them carries a request body per RFC 9110 (<c>TRACE</c> is forbidden one
    ///         outright), and the docs actively point at <c>[HttpEndpoint("OPTIONS", …)]</c> as the way
    ///         to declare the verbs with no dedicated attribute. Kept in step with
    ///         <c>EndpointsGenerator.IsDeclaredBodylessVerb</c> on the generator side, which makes the
    ///         same call about whether to emit a request-body read; the two cannot share code (the
    ///         generator targets netstandard2.0 and does not reference this assembly), so they are kept
    ///         in sync by hand and each points at the other.
    ///     </para>
    ///     <para>
    ///         <b>All</b>, not <b>any</b>: a hypothetical endpoint declaring both a bodyless and a
    ///         body-carrying verb (say <c>GET</c> and <c>POST</c>) genuinely does accept a JSON body on
    ///         one of them, so declaring <c>Accepts</c> is the accurate answer — under "any" it would
    ///         have been silently omitted. This is unreachable today: both routes into
    ///         <c>EndpointConfiguration.HttpMethods</c> produce exactly one verb (a route attribute
    ///         carries a single <c>Method</c>, and <c>EndpointBuilder.Route</c> assigns a
    ///         single-element array), so "all" and "any" cannot currently disagree. It is settled this
    ///         way now, while the question is free, rather than left as a latent wrong answer for
    ///         whoever adds multi-verb support. Note that a request with no <c>Content-Type</c> at all
    ///         matches an endpoint regardless of its <c>Accepts</c> declaration, so declaring
    ///         <c>Accepts</c> on such an endpoint would not cause its bodyless verb to be rejected.
    ///     </para>
    /// </remarks>
    internal static bool AllVerbsAreBodyless(string[] httpMethods)
    {
        foreach (var method in httpMethods)
        {
            if (method is not ("GET" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE"))
            {
                return false;
            }
        }

        return true;
    }
}
