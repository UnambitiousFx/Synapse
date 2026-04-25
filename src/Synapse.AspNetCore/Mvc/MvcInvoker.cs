using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.AspNetCore.Mvc;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore.Mvc;

internal sealed class MvcInvoker : IMvcInvoker
{
    private readonly IFailureHttpMapper _failureMapper;
    private readonly IInvoker _invoker;

    public MvcInvoker(IInvoker invoker, IFailureHttpMapper failureMapper)
    {
        _invoker = invoker;
        _failureMapper = failureMapper;
    }

    public async ValueTask<IActionResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default) where TResponse : notnull
    {
        return await _invoker.InvokeAsync(request, cancellationToken)
            .AsActionResultBuilder(_failureMapper);
    }

    public async ValueTask<IActionResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        Func<TResponse, IActionResult> onSuccess, CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        if (result.TryGet(out var value, out _))
        {
            return onSuccess(value!);
        }

        return await ValueTask.FromResult(result).AsActionResultBuilder(_failureMapper);
    }

    public async ValueTask<IActionResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        return await _invoker.InvokeAsync(request, cancellationToken)
            .AsActionResultBuilder(_failureMapper);
    }
}