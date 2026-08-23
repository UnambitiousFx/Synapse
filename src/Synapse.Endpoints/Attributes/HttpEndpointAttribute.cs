namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Declares the HTTP method and route template of an endpoint. Use the verb-specific
///     attributes (<see cref="GetAttribute" />, <see cref="PostAttribute" />, …) for common
///     methods and this attribute for the rest.
/// </summary>
/// <remarks>
///     The route template must be a constant expression. That is what lets the analyzer check
///     route parameters against the message's properties at compile time. An endpoint whose route
///     must be computed can omit this attribute and declare the route in <c>Configure</c> instead;
///     declaring it in both places is reported as SYNE009.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class HttpEndpointAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="HttpEndpointAttribute" /> class.</summary>
    /// <param name="method">The HTTP method. Case-insensitive; stored uppercase.</param>
    /// <param name="route">The route template, for example <c>/tasks/{id:guid}</c>.</param>
    public HttpEndpointAttribute(string method,
        string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        Method = method.ToUpperInvariant();
        Route = route;
    }

    /// <summary>Gets the uppercase HTTP method.</summary>
    public string Method { get; }

    /// <summary>Gets the route template.</summary>
    public string Route { get; }
}
