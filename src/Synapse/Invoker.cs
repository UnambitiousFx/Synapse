using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse;

internal sealed class Invoker(IDependencyResolver resolver, IOptions<InvokerOptions> options) : IInvoker
{
    public ValueTask<Result<TResponse>> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        var requestType = request.GetType();
        if (!options.Value.RequestDispatchers.TryGetValue(requestType, out var del))
        {
            throw new InvalidOperationException(
                $"No handler registered for request type '{requestType.Name}'. " +
                $"Ensure it is registered via cfg.RegisterRequestHandler<THandler, {requestType.Name}, TResponse>().");
        }

        var dispatch =
            (Func<IRequest<TResponse>, IDependencyResolver, CancellationToken, ValueTask<Result<TResponse>>>)del;
        return dispatch(request, resolver, cancellationToken);
    }

    public ValueTask<Result> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var requestType = request.GetType();
        if (!options.Value.VoidRequestDispatchers.TryGetValue(requestType, out var del))
        {
            throw new InvalidOperationException(
                $"No handler registered for request type '{requestType.Name}'. " +
                $"Ensure it is registered via cfg.RegisterRequestHandler<THandler, {requestType.Name}>().");
        }

        var dispatch =
            (Func<IRequest, IDependencyResolver, CancellationToken, ValueTask<Result>>)del;
        return dispatch(request, resolver, cancellationToken);
    }

    public async IAsyncEnumerable<Result<TItem>> InvokeStreamAsync<TItem>(IStreamRequest<TItem> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TItem : notnull
    {
        var requestType = request.GetType();
        if (!options.Value.StreamRequestDispatchers.TryGetValue(requestType, out var del))
        {
            throw new InvalidOperationException(
                $"No stream handler registered for request type '{requestType.Name}'. " +
                $"Ensure it is registered via cfg.RegisterStreamRequestHandler<THandler, {requestType.Name}, TItem>().");
        }

        var dispatch =
            (Func<IStreamRequest<TItem>, IDependencyResolver, CancellationToken, IAsyncEnumerable<Result<TItem>>>)del;
        await foreach (var item in dispatch(request, resolver, cancellationToken))
        {
            yield return item;
        }
    }
}