using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional.AspNetCore.Http;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore.Http;

internal sealed class HttpInvoker : IHttpInvoker
{
    private readonly IFailureHttpMapper _failureMapper;
    private readonly IInvoker _invoker;

    public HttpInvoker(IInvoker invoker,
        IFailureHttpMapper failureMapper)
    {
        _invoker = invoker;
        _failureMapper = failureMapper;
    }

    public async ValueTask<IResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default) where TResponse : notnull
    {
        return await _invoker.InvokeAsync(request, cancellationToken)
            .AsHttpBuilder(_failureMapper);
    }

    public async ValueTask<IResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        Func<TResponse, IResult> onSuccess, CancellationToken cancellationToken = default) where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        if (result.TryGet(out var value, out _))
        {
            return onSuccess(value!);
        }

        return await ValueTask.FromResult(result).AsHttpBuilder(_failureMapper);
    }

    public async ValueTask<IResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        return await _invoker.InvokeAsync(request, cancellationToken)
            .AsHttpBuilder(_failureMapper);
    }

    public async IAsyncEnumerable<TItem> InvokeStreamAsync<TItem>(IStreamRequest<TItem> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where TItem : notnull
    {
        await foreach (var result in _invoker.InvokeStreamAsync(request, cancellationToken))
        {
            if (result.TryGet(out var item, out _))
            {
                yield return item!;
            }
        }
    }
}