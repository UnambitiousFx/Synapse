namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Outcome of binding an HTTP request onto a message. A struct so the success path allocates
///     nothing beyond the message itself.
/// </summary>
/// <typeparam name="T">The bound message type.</typeparam>
public readonly struct BindResult<T>
{
    private BindResult(bool isSuccess,
        T? value,
        string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>Gets a value indicating whether binding succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the bound message, valid only when <see cref="IsSuccess" /> is true.</summary>
    public T? Value { get; }

    /// <summary>Gets the failure description, valid only when <see cref="IsSuccess" /> is false.</summary>
    public string? Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The bound message.</param>
    /// <returns>A successful <see cref="BindResult{T}" />.</returns>
    public static BindResult<T> Success(T value)
    {
        return new BindResult<T>(true, value, null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">A description suitable for a problem-details response.</param>
    /// <returns>A failed <see cref="BindResult{T}" />.</returns>
    public static BindResult<T> Failure(string error)
    {
        return new BindResult<T>(false, default, error);
    }
}
