using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Outcome of binding an HTTP request onto a message. A struct so the success path allocates
///     nothing beyond the message itself.
/// </summary>
/// <typeparam name="T">The bound message type.</typeparam>
/// <remarks>
///     A failure carries every problem found, keyed by field, rather than only the first. Presence and
///     parse failures across route, query and header values accumulate; a body that cannot be read at
///     all is reported alone under the <c>body</c> key, because without a deserialized message there
///     is nothing left to bind the remaining values onto.
/// </remarks>
public readonly struct BindResult<T>
{
    private readonly Dictionary<string, string[]>? _errors;

    private BindResult(bool isSuccess,
        T? value,
        Dictionary<string, string[]>? errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        _errors = errors;
    }

    /// <summary>Gets a value indicating whether binding succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the bound message, valid only when <see cref="IsSuccess" /> is true.</summary>
    public T? Value { get; }

    /// <summary>
    ///     Gets the collected failures keyed by field name, valid only when <see cref="IsSuccess" /> is
    ///     false.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors => _errors;

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The bound message.</param>
    /// <returns>A successful <see cref="BindResult{T}" />.</returns>
    public static BindResult<T> Success(T value)
    {
        return new BindResult<T>(true, value, null);
    }

    /// <summary>Creates a failed result carrying one message.</summary>
    /// <param name="field">The field the message is about; use <c>body</c> for the request body.</param>
    /// <param name="message">A description suitable for a problem-details response.</param>
    /// <returns>A failed <see cref="BindResult{T}" />.</returns>
    /// <exception cref="ArgumentException"><paramref name="field" /> is null or whitespace.</exception>
    public static BindResult<T> Failure(string field,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        return new BindResult<T>(
            false,
            default,
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
    }

    /// <summary>Creates a failed result carrying another result's failures, retyped.</summary>
    /// <typeparam name="TSource">The type the failure was originally produced for.</typeparam>
    /// <param name="source">The failed result to carry over; it must actually be a failure.</param>
    /// <returns>A failed <see cref="BindResult{T}" /> with <paramref name="source" />'s errors.</returns>
    /// <exception cref="ArgumentException"><paramref name="source" /> succeeded, so it describes no failure.</exception>
    /// <remarks>
    ///     A generated form binder reads the body as <see cref="BindResult{T}" /> of
    ///     <c>IFormCollection</c> and must return one of the message type. Without this the emitter had
    ///     to substitute a constant message, which made every reason <see cref="BindingHelpers.ReadFormAsync" />
    ///     builds — the content-type guidance, the malformed-body detail — unreachable from generated
    ///     code. The JSON path never needed it because <c>ReadJsonBodyAsync&lt;TRequest&gt;</c> already
    ///     returns the right closed type.
    /// </remarks>
    public static BindResult<T> Failure<TSource>(BindResult<TSource> source)
    {
        if (source._errors is null)
        {
            throw new ArgumentException(
                "The source result succeeded, so it does not describe a failure.",
                nameof(source));
        }

        // The dictionary is copied rather than shared: BindResult is a struct handed around by value,
        // and two results aliasing one mutable dictionary would make either one's Problem() depend on
        // what happened to the other.
        return new BindResult<T>(
            false,
            default,
            new Dictionary<string, string[]>(source._errors, StringComparer.Ordinal));
    }

    /// <summary>Creates a failed result carrying everything a validator collected.</summary>
    /// <param name="validator">The validator, which must have collected at least one error.</param>
    /// <returns>A failed <see cref="BindResult{T}" />.</returns>
    /// <exception cref="ArgumentException">The validator collected nothing.</exception>
    public static BindResult<T> Failure(BindingValidator validator)
    {
        var errors = validator.Errors;
        if (errors is null)
        {
            throw new ArgumentException(
                "The validator collected no errors, so it does not describe a failure.",
                nameof(validator));
        }

        return new BindResult<T>(
            false,
            default,
            errors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    /// <summary>Builds the <c>400</c> response describing this failure.</summary>
    /// <returns>A <c>400 Bad Request</c> carrying <c>HttpValidationProblemDetails</c>.</returns>
    /// <exception cref="InvalidOperationException">Binding succeeded, so there is no problem to describe.</exception>
    public IResult Problem()
    {
        if (_errors is null)
        {
            throw new InvalidOperationException(
                "Binding succeeded, so there is nothing to report. Check IsSuccess before calling Problem.");
        }

        return TypedResults.ValidationProblem(_errors);
    }
}
