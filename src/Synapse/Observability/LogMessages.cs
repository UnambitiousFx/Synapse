using Microsoft.Extensions.Logging;

namespace UnambitiousFx.Synapse.Observability;

/// <summary>
///     Provides structured logging methods using LoggerMessage source generation for dispatch operations.
/// </summary>
public static partial class LogMessages
{
    /// <summary>
    ///     Logs when a message is being published to a transport.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageType">The type of message being published.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="transportName">The name of the transport.</param>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Publishing message {MessageType} with ID {MessageId} to transport {TransportName}")]
    public static partial void LogPublishing(
        ILogger logger,
        string messageType,
        string messageId,
        string transportName);

    /// <summary>
    ///     Logs when transport dispatch fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageType">The type of message that failed.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Transport dispatch failed for {MessageType} with ID {MessageId}: {Error}")]
    public static partial void LogTransportFailure(
        ILogger logger,
        string messageType,
        string messageId,
        string error);

    /// <summary>
    ///     Logs when a message is received from a transport.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageType">The type of message received.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="transportName">The name of the transport.</param>
    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Received message {MessageType} with ID {MessageId} from transport {TransportName}")]
    public static partial void LogReceived(
        ILogger logger,
        string messageType,
        string messageId,
        string transportName);

    /// <summary>
    ///     Logs when a message processing is being retried.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageType">The type of message being retried.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="attemptCount">The current attempt count.</param>
    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Retrying message {MessageType} with ID {MessageId}, attempt {AttemptCount}")]
    public static partial void LogRetry(
        ILogger logger,
        string messageType,
        string messageId,
        int attemptCount);

    /// <summary>
    ///     Logs when a message is sent to the dead-letter queue.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageType">The type of message being dead-lettered.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="reason">The reason for dead-lettering.</param>
    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Message {MessageType} with ID {MessageId} sent to dead-letter queue: {Reason}")]
    public static partial void LogDeadLetter(
        ILogger logger,
        string messageType,
        string messageId,
        string reason);
}