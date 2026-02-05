using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse.Send;

internal sealed class Sender(IDependencyResolver resolver) : ISender
{
    public ResultTask<TResponse> SendAsync<TRequest, TResponse>(TRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
    {
        var handler = resolver.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        return handler.HandleAsync(request, cancellationToken)
            .AsAsync();
    }

    public ResultTask SendAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var handler = resolver.GetRequiredService<IRequestHandler<TRequest>>();
        return handler.HandleAsync(request, cancellationToken).AsAsync();
    }

    public async IAsyncEnumerable<Result<TItem>> SendStreamAsync<TRequest, TItem>(TRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        var handler = resolver.GetRequiredService<IStreamRequestHandler<TRequest, TItem>>();
        await foreach (var item in handler.HandleAsync(request, cancellationToken)) yield return item;
    }
}