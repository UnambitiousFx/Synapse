using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a delegate that handles a request and returns a response
/// </summary>
/// <typeparam name="TResponse">The type of response</typeparam>
/// <returns>A task containing the response</returns>
public delegate ValueTask<Result<TResponse>> RequestHandlerDelegate<TResponse>()
    where TResponse : notnull;

/// <summary>
///     Represents a delegate that processes a specific handler logic and produces a result,
///     optionally including a response when used with a generic version.
/// </summary>
/// <returns>A <see cref="ValueTask" /> containing the processing result.</returns>
public delegate ValueTask<Result> RequestHandlerDelegate();