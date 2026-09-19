using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Internal;

/// <summary>
///     Dispatches messages to delegates a test registered, instead of to real handlers.
/// </summary>
/// <remarks>
///     The only service the harness fakes. <see cref="AspNetCore.Http.IHttpInvoker" /> and the
///     registered <c>IFailureHttpMapper</c> above it are the real ones, so a stub returning a failed
///     <see cref="Result" /> produces the same status an application produces for the same failure.
///     Three maps rather than one because a message is dispatched through exactly one of the three
///     shapes, and keeping them apart is what lets each lookup name the right <c>Handle</c> overload
///     when nothing is registered.
/// </remarks>
internal sealed class StubInvoker : IInvoker
{
    private readonly Dictionary<Type, object> _value = [];
    private readonly Dictionary<Type, object> _void = [];
    private readonly Dictionary<Type, object> _stream = [];

    /// <summary>Registers the delegate that answers a value-returning message.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">The delegate.</param>
    internal void RegisterValue<TRequest, TResponse>(
        Func<object, CancellationToken, ValueTask<Result<TResponse>>> handler)
        where TResponse : notnull
    {
        _value[typeof(TRequest)] = handler;
    }

    /// <summary>Registers the delegate that answers a void command.</summary>
    /// <typeparam name="TRequest">The command type.</typeparam>
    /// <param name="handler">The delegate.</param>
    internal void RegisterVoid<TRequest>(Func<object, CancellationToken, ValueTask<Result>> handler)
    {
        _void[typeof(TRequest)] = handler;
    }

    /// <summary>Registers the delegate that answers a streaming message.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <typeparam name="TItem">The streamed item type.</typeparam>
    /// <param name="handler">The delegate.</param>
    internal void RegisterStream<TRequest, TItem>(
        Func<object, CancellationToken, IAsyncEnumerable<Result<TItem>>> handler)
        where TItem : notnull
    {
        _stream[typeof(TRequest)] = handler;
    }

    /// <inheritdoc />
    public ValueTask<Result<TResponse>> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        var handler = Resolve<TResponse>(_value, request.GetType(), typeof(TResponse).Name);
        return handler(request, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<Result> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var type = request!.GetType();
        if (!_void.TryGetValue(type, out var registered))
        {
            throw NotStubbed(type, $"options.Handle<{type.Name}>(…)");
        }

        if (registered is not Func<object, CancellationToken, ValueTask<Result>> handler)
        {
            throw WrongShape(type, "Result", $"options.Handle<{type.Name}>(…)");
        }

        return handler(request, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Result<TItem>> InvokeStreamAsync<TItem>(IStreamRequest<TItem> request,
        CancellationToken cancellationToken = default)
        where TItem : notnull
    {
        var type = request.GetType();
        if (!_stream.TryGetValue(type, out var registered))
        {
            throw NotStubbed(type, $"options.HandleStream<{type.Name}, {typeof(TItem).Name}>(…)");
        }

        if (registered is not Func<object, CancellationToken, IAsyncEnumerable<Result<TItem>>> handler)
        {
            throw WrongShape(type, $"IAsyncEnumerable<Result<{typeof(TItem).Name}>>",
                $"options.HandleStream<{type.Name}, {typeof(TItem).Name}>(…)");
        }

        return handler(request, cancellationToken);
    }

    private static Func<object, CancellationToken, ValueTask<Result<TResponse>>> Resolve<TResponse>(
        Dictionary<Type, object> handlers,
        Type requestType,
        string responseName)
        where TResponse : notnull
    {
        var call = $"options.Handle<{requestType.Name}, {responseName}>(…)";

        if (!handlers.TryGetValue(requestType, out var registered))
        {
            throw NotStubbed(requestType, call);
        }

        if (registered is not Func<object, CancellationToken, ValueTask<Result<TResponse>>> handler)
        {
            throw WrongShape(requestType, $"Result<{responseName}>", call);
        }

        return handler;
    }

    private static InvalidOperationException NotStubbed(Type requestType,
        string call)
    {
        return new InvalidOperationException(
            $"No handler is stubbed for '{requestType.Name}'. The harness dispatches messages to " +
            $"delegates a test registers rather than to real handlers, so register one with {call} " +
            "when creating the harness.");
    }

    private static InvalidOperationException WrongShape(Type requestType,
        string expected,
        string call)
    {
        return new InvalidOperationException(
            $"The handler stubbed for '{requestType.Name}' does not return {expected}, which is what " +
            $"the endpoint dispatches it as. Register it with {call} instead.");
    }
}
