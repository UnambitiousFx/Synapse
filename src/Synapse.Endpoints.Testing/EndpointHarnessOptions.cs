using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Testing.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Testing;

/// <summary>
///     Configures a harness before its pipeline is built.
/// </summary>
public sealed class EndpointHarnessOptions
{
    /// <summary>
    ///     Gets the services the endpoint resolves from, pre-seeded with routing, logging, options and
    ///     the Synapse ASP.NET Core services.
    /// </summary>
    /// <remarks>
    ///     Register whatever the endpoint reads through <c>context.Service&lt;T&gt;()</c> here. JSON
    ///     serialization is configured the same way it is in an application, with
    ///     <c>ConfigureHttpJsonOptions</c>.
    /// </remarks>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>Gets the stubbed invoker the harness registers as <see cref="IInvoker" />.</summary>
    internal StubInvoker Invoker { get; } = new();

    /// <summary>Stubs the handler for a message that returns a response.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">Returns the result the handler would have returned.</param>
    /// <returns>The options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    public EndpointHarnessOptions Handle<TRequest, TResponse>(Func<TRequest, Result<TResponse>> handler)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Handle<TRequest, TResponse>((request, _) => ValueTask.FromResult(handler(request)));
    }

    /// <summary>Stubs the asynchronous handler for a message that returns a response.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">Returns the result the handler would have returned.</param>
    /// <returns>The options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    public EndpointHarnessOptions Handle<TRequest, TResponse>(
        Func<TRequest, CancellationToken, ValueTask<Result<TResponse>>> handler)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        Invoker.RegisterValue<TRequest, TResponse>((request, token) => handler((TRequest)request, token));
        return this;
    }

    /// <summary>Stubs the handler for a command with no response.</summary>
    /// <typeparam name="TRequest">The command type.</typeparam>
    /// <param name="handler">Returns the result the handler would have returned.</param>
    /// <returns>The options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    public EndpointHarnessOptions Handle<TRequest>(Func<TRequest, Result> handler)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Handle<TRequest>((request, _) => ValueTask.FromResult(handler(request)));
    }

    /// <summary>Stubs the asynchronous handler for a command with no response.</summary>
    /// <typeparam name="TRequest">The command type.</typeparam>
    /// <param name="handler">Returns the result the handler would have returned.</param>
    /// <returns>The options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    public EndpointHarnessOptions Handle<TRequest>(
        Func<TRequest, CancellationToken, ValueTask<Result>> handler)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(handler);

        Invoker.RegisterVoid<TRequest>((request, token) => handler((TRequest)request, token));
        return this;
    }

    /// <summary>Stubs the handler for a streaming message with a sequence of successful items.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <typeparam name="TItem">The streamed item type.</typeparam>
    /// <param name="handler">Returns the items the handler would have streamed.</param>
    /// <returns>The options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    public EndpointHarnessOptions HandleStream<TRequest, TItem>(Func<TRequest, IEnumerable<TItem>> handler)
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        return HandleStream<TRequest, TItem>(request => Stream(handler(request)));

        static async IAsyncEnumerable<Result<TItem>> Stream(IEnumerable<TItem> items)
        {
            foreach (var item in items)
            {
                yield return Result.Success(item);
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>Stubs the handler for a streaming message, including per-item failures.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <typeparam name="TItem">The streamed item type.</typeparam>
    /// <param name="handler">Returns the results the handler would have streamed.</param>
    /// <returns>The options, for chaining.</returns>
    /// <remarks>
    ///     A failed item is skipped by <c>IHttpInvoker.InvokeStreamAsync</c> rather than surfaced, and
    ///     the harness keeps that behaviour; this overload exists so a test can exercise it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    public EndpointHarnessOptions HandleStream<TRequest, TItem>(
        Func<TRequest, IAsyncEnumerable<Result<TItem>>> handler)
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        Invoker.RegisterStream<TRequest, TItem>((request, _) => handler((TRequest)request));
        return this;
    }
}
