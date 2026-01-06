namespace UnambitiousFx.Synapse.Abstractions.Exceptions;

/// <summary>
///     Base class for all mediator-related exceptions.
/// </summary>
public abstract class SynapseException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SynapseException" /> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    protected SynapseException(string message)
        : base(message)
    {
    }
}