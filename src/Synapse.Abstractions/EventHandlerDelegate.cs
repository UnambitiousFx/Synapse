using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a delegate that serves as a handler for processing events within the mediator pipeline.
///     It encapsulates an asynchronous operation that returns a result of type `Result`.
///     This delegate is typically executed as part of the event processing pipeline and is designed
///     to handle events in a chain or pipeline-like execution flow. It often gets invoked with
///     preceding or succeeding operations within the pipeline.
///     The handler is expected to perform its logic and return a <see cref="ValueTask{Result}" /> indicating
///     the final status of the operation:
///     - A successful execution should return a `Result.Success()`
///     - A failed execution should return a `Result.Failure()` with detailed error information.
///     Commonly used in conjunction with pipeline behaviors and event handlers in the mediator framework.
/// </summary>
public delegate ValueTask<Result> EventHandlerDelegate();