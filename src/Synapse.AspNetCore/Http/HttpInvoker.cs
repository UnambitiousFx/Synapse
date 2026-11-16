using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Http;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.Abstractions;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Synapse.AspNetCore.Http;

internal sealed class HttpInvoker : IHttpInvoker
{
    private readonly string? _challenge;
    private readonly IFailureHttpMapper _failureMapper;
    private readonly IInvoker _invoker;

    public HttpInvoker(IInvoker invoker,
        IFailureHttpMapper failureMapper,
        IOptions<SynapseAspNetCoreOptions> options)
    {
        _invoker = invoker;
        _failureMapper = failureMapper;
        _challenge = options.Value.AuthenticationChallenge;
    }

    public async ValueTask<IHttpResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default) where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        var httpResult = await result.AsHttpBuilder(_failureMapper);
        return result.IsSuccess ? httpResult : new ChallengingHttpResult(httpResult, _challenge);
    }

    public async ValueTask<IHttpResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        Func<TResponse, IHttpResult> onSuccess, CancellationToken cancellationToken = default) where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        if (result.TryGet(out var value, out _))
        {
            return onSuccess(value!);
        }

        return new ChallengingHttpResult(await result.AsHttpBuilder(_failureMapper), _challenge);
    }

    public async ValueTask<IHttpResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        var httpResult = await result.AsHttpBuilder(_failureMapper);
        return result.IsSuccess ? httpResult : new ChallengingHttpResult(httpResult, _challenge);
    }

    public ValueTask<IHttpResult> InvokeStreamAsync<TItem>(IStreamRequest<TItem> request,
        CancellationToken cancellationToken = default) where TItem : notnull
    {
        // The stream is opened only when the result executes, so a result an endpoint filter discards opens nothing.
        return ValueTask.FromResult<IHttpResult>(new StreamHttpResult<TItem>(this, request, cancellationToken));
    }

    private sealed class StreamHttpResult<TItem> : IHttpResult where TItem : notnull
    {
        private readonly CancellationToken _cancellationToken;
        private readonly HttpInvoker _owner;
        private readonly IStreamRequest<TItem> _request;

        public StreamHttpResult(HttpInvoker owner, IStreamRequest<TItem> request, CancellationToken cancellationToken)
        {
            _owner = owner;
            _request = request;
            _cancellationToken = cancellationToken;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await using var items = _owner._invoker.InvokeStreamAsync(_request, _cancellationToken)
                .GetAsyncEnumerator(_cancellationToken);

            // The first item decides the status: once the body starts, a failure can no longer change it.
            var hasFirst = await items.MoveNextAsync();
            if (hasFirst && !items.Current.TryGet(out _, out _))
            {
                var failure = await items.Current.AsHttpBuilder(_owner._failureMapper);
                await new ChallengingHttpResult(failure, _owner._challenge).ExecuteAsync(httpContext);
                return;
            }

            // Typed as IAsyncEnumerable even when empty, so one JsonTypeInfo covers every case under Native AOT.
            await TypedResults.Ok(Remaining(items, hasFirst)).ExecuteAsync(httpContext);
        }

        private static async IAsyncEnumerable<TItem> Remaining(IAsyncEnumerator<Result<TItem>> items, bool hasFirst)
        {
            if (!hasFirst)
            {
                yield break;
            }

            do
            {
                if (!items.Current.TryGet(out var item, out var failure))
                {
                    // The 200 is already on the wire. Throwing aborts the response, so the client sees a truncated
                    // body instead of a complete-looking one with items silently missing.
                    throw new InvalidOperationException(
                        $"The stream failed after the response started: {failure}");
                }

                yield return item!;
            } while (await items.MoveNextAsync());
        }
    }
}
