using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Http;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.Abstractions;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

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

    public async ValueTask<IHttpResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default) where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        var httpResult = await result.AsHttpBuilder(_failureMapper);
        return result.IsSuccess ? httpResult : new ChallengingHttpResult(httpResult);
    }

    public async ValueTask<IHttpResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        Func<TResponse, IHttpResult> onSuccess, CancellationToken cancellationToken = default) where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        if (result.TryGet(out var value, out _))
        {
            return onSuccess(value!);
        }

        return new ChallengingHttpResult(await result.AsHttpBuilder(_failureMapper));
    }

    public async ValueTask<IHttpResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        var httpResult = await result.AsHttpBuilder(_failureMapper);
        return result.IsSuccess ? httpResult : new ChallengingHttpResult(httpResult);
    }

    public async ValueTask<IHttpResult> InvokeStreamAsync<TItem>(IStreamRequest<TItem> request,
        CancellationToken cancellationToken = default) where TItem : notnull
    {
        // The status is chosen from the first item: once the body starts, a failure can no longer change it.
        var items = _invoker.InvokeStreamAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        if (!await items.MoveNextAsync())
        {
            await items.DisposeAsync();
            return TypedResults.Ok(Array.Empty<TItem>());
        }

        var first = items.Current;
        if (!first.TryGet(out var firstItem, out _))
        {
            await items.DisposeAsync();
            return new ChallengingHttpResult(await first.AsHttpBuilder(_failureMapper));
        }

        return TypedResults.Ok(Remaining(firstItem!, items));
    }

    private static async IAsyncEnumerable<TItem> Remaining<TItem>(TItem first, IAsyncEnumerator<Result<TItem>> items)
        where TItem : notnull
    {
        await using (items)
        {
            yield return first;
            while (await items.MoveNextAsync())
            {
                if (!items.Current.TryGet(out var item, out var failure))
                {
                    // The 200 is already on the wire. Throwing aborts the response, so the client sees a truncated
                    // body instead of a complete-looking one with items silently missing.
                    throw new InvalidOperationException(
                        $"The stream failed after the response started: {failure}");
                }

                yield return item!;
            }
        }
    }
}
