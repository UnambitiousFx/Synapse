using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Assertions;

/// <summary>
///     Assertions over the errors a binding failure collected.
/// </summary>
public sealed class ValidationProblemAssertions
{
    private readonly EndpointResponse _response;
    private readonly HttpValidationProblemDetails _problem;

    /// <summary>Initializes a new instance of the <see cref="ValidationProblemAssertions" /> class.</summary>
    /// <param name="response">The response the problem was read from.</param>
    /// <param name="problem">The problem details.</param>
    internal ValidationProblemAssertions(EndpointResponse response,
        HttpValidationProblemDetails problem)
    {
        _response = response;
        _problem = problem;
    }

    /// <summary>Asserts that a field carries exactly <paramref name="message" />.</summary>
    /// <param name="field">The field name, as the binder reported it.</param>
    /// <param name="message">The expected message.</param>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The field is absent or carries a different message.</exception>
    public ValidationProblemAssertions WithError(string field,
        string message)
    {
        WithErrorFor(field);

        if (!_problem.Errors[field].Contains(message, StringComparer.Ordinal))
        {
            throw Failed($"Expected '{field}' to carry the error '{message}' but it carried " +
                         $"{Render(_problem.Errors[field])}");
        }

        return this;
    }

    /// <summary>Asserts that a field carries at least one error.</summary>
    /// <param name="field">The field name, as the binder reported it.</param>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">The field carries no error.</exception>
    public ValidationProblemAssertions WithErrorFor(string field)
    {
        if (!_problem.Errors.ContainsKey(field))
        {
            throw Failed($"Expected an error for '{field}' but errors were collected for " +
                         $"{Render(_problem.Errors.Keys)}");
        }

        return this;
    }

    /// <summary>Asserts how many fields carry errors.</summary>
    /// <param name="count">The expected number of fields.</param>
    /// <returns>The assertions, for chaining.</returns>
    /// <exception cref="EndpointAssertionException">A different number of fields carry errors.</exception>
    /// <remarks>
    ///     Counts fields, not messages: binding accumulates per field, and this is the assertion that
    ///     catches an endpoint reporting only the first of several bad inputs.
    /// </remarks>
    public ValidationProblemAssertions WithErrorCount(int count)
    {
        if (_problem.Errors.Count != count)
        {
            throw Failed($"Expected errors for {count} field(s) but got {_problem.Errors.Count}: " +
                         $"{Render(_problem.Errors.Keys)}");
        }

        return this;
    }

    private static string Render(IEnumerable<string> values)
    {
        return $"[{string.Join(", ", values.Select(value => $"'{value}'"))}]";
    }

    private EndpointAssertionException Failed(string message)
    {
        return new EndpointResponseAssertions(_response).Failed(message);
    }
}
