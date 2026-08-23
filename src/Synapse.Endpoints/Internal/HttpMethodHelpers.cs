namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>Shared helpers used when declaring OpenAPI metadata from an endpoint's HTTP methods.</summary>
internal static class HttpMethodHelpers
{
    /// <summary>
    ///     Determines whether any of <paramref name="httpMethods" /> conventionally carries no
    ///     request body, so declaring <c>Accepts</c> for it would misrepresent the endpoint.
    /// </summary>
    /// <param name="httpMethods">The endpoint's declared HTTP methods.</param>
    /// <returns><see langword="true" /> when a bodyless verb is present.</returns>
    internal static bool IsBodylessVerb(string[] httpMethods)
    {
        foreach (var method in httpMethods)
        {
            if (method is "GET" or "DELETE" or "HEAD")
            {
                return true;
            }
        }

        return false;
    }
}
