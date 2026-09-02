using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Assertions;

/// <summary>
///     Assertions over what an endpoint wrote.
/// </summary>
/// <remarks>
///     Every failure message ends with the actual status, content type and body. That is the reason
///     this exists rather than a bare equality assertion: an unexpected <c>400</c> should show the
///     validation errors that caused it without needing a second run.
/// </remarks>
public sealed class EndpointResponseAssertions
{
    private readonly EndpointResponse _response;

    /// <summary>Initializes a new instance of the <see cref="EndpointResponseAssertions" /> class.</summary>
    /// <param name="response">The response to assert on.</param>
    internal EndpointResponseAssertions(EndpointResponse response)
    {
        _response = response;
    }

    /// <summary>Asserts the status code.</summary>
    /// <param name="statusCode">The expected status.</param>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The status differs.</exception>
    public EndpointResponseAssertions Status(int statusCode)
    {
        if (_response.StatusCode != statusCode)
        {
            throw Failed($"Expected status {statusCode} but got {_response.StatusCode}");
        }

        return this;
    }

    /// <summary>Asserts a <c>200 OK</c>.</summary>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The status differs.</exception>
    public EndpointResponseAssertions Ok()
    {
        return Status(StatusCodes.Status200OK);
    }

    /// <summary>Asserts a <c>201 Created</c>.</summary>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The status differs.</exception>
    public EndpointResponseAssertions Created()
    {
        return Status(StatusCodes.Status201Created);
    }

    /// <summary>Asserts a <c>204 No Content</c>.</summary>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The status differs.</exception>
    public EndpointResponseAssertions NoContent()
    {
        return Status(StatusCodes.Status204NoContent);
    }

    /// <summary>Asserts a <c>404 Not Found</c>.</summary>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The status differs.</exception>
    public EndpointResponseAssertions NotFound()
    {
        return Status(StatusCodes.Status404NotFound);
    }

    /// <summary>Asserts the response content type contains <paramref name="contentType" />.</summary>
    /// <param name="contentType">The expected media type.</param>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The content type differs.</exception>
    public EndpointResponseAssertions ContentType(string contentType)
    {
        if (_response.ContentType?.Contains(contentType, StringComparison.OrdinalIgnoreCase) != true)
        {
            throw Failed($"Expected content type containing '{contentType}' but got " +
                         $"'{_response.ContentType ?? "none"}'");
        }

        return this;
    }

    /// <summary>Asserts a response header's value.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The expected value.</param>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The header is missing or differs.</exception>
    public EndpointResponseAssertions Header(string name,
        string value)
    {
        if (!_response.Headers.TryGetValue(name, out var actual))
        {
            throw Failed($"Expected header '{name}' to be '{value}' but no such header was written");
        }

        if (!string.Equals(actual.ToString(), value, StringComparison.Ordinal))
        {
            throw Failed($"Expected header '{name}' to be '{value}' but it was '{actual}'");
        }

        return this;
    }

    /// <summary>Deserializes the body and returns it, so the test can assert on the value.</summary>
    /// <typeparam name="TValue">The type to deserialize to.</typeparam>
    /// <returns>The deserialized body.</returns>
    /// <exception cref="EndpointAssertionException">The body could not be read as <typeparamref name="TValue" />.</exception>
    public TValue Json<TValue>()
    {
        try
        {
            return _response.ReadJson<TValue>()
                   ?? throw Failed($"Expected a '{typeof(TValue).Name}' body but it was null");
        }
        catch (InvalidOperationException exception)
        {
            // ReadJson's own message already restates the status, content type and body, so
            // chaining it as-is would print that description twice once this exception's
            // InnerException is rendered alongside its own Describe()-appended message. Its
            // InnerException — the actual JsonException — carries the real parse diagnostics
            // without repeating the description, so that is what stays attached here.
            throw Failed($"Expected a '{typeof(TValue).Name}' body but it could not be read",
                exception.InnerException ?? exception);
        }
    }

    /// <summary>Asserts a <c>400</c> validation problem and returns assertions over its errors.</summary>
    /// <returns>Assertions over the collected errors.</returns>
    /// <exception cref="EndpointAssertionException">The status is not <c>400</c>, or the body is not a validation problem.</exception>
    public ValidationProblemAssertions ValidationProblem()
    {
        Status(StatusCodes.Status400BadRequest);

        try
        {
            return new ValidationProblemAssertions(_response, _response.ReadValidationProblem());
        }
        catch (InvalidOperationException exception)
        {
            throw Failed("Expected a validation problem body", exception);
        }
    }

    /// <summary>Builds the failure message, appending the actual response.</summary>
    /// <param name="message">What was expected.</param>
    /// <returns>The exception to throw.</returns>
    internal EndpointAssertionException Failed(string message)
    {
        return new EndpointAssertionException($"{message}. {Describe()}");
    }

    private EndpointAssertionException Failed(string message,
        Exception innerException)
    {
        return new EndpointAssertionException($"{message}. {Describe()}", innerException);
    }

    private string Describe()
    {
        var body = _response.Body.Length == 0 ? "(empty body)" : _response.Body;
        return $"The response was {_response.StatusCode} " +
               $"({_response.ContentType ?? "no content type"}): {body}";
    }
}
