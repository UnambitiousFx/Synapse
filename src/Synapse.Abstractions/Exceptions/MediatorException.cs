namespace UnambitiousFx.Synapse.Abstractions.Exceptions;

/// <summary>
///     Base class for all mediator-related exceptions.
/// </summary>
public abstract class MediatorException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MediatorException" /> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    protected MediatorException(string message)
        : base(message)
    {
    }
}