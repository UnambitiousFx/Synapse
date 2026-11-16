using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.AspNetCore.Mvc;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;

namespace UnambitiousFx.Synapse.AspNetCore.Mvc;

internal sealed class MvcInvoker : IMvcInvoker
{
    private readonly string? _challenge;
    private readonly IFailureHttpMapper _failureMapper;
    private readonly IInvoker _invoker;

    public MvcInvoker(IInvoker invoker, IFailureHttpMapper failureMapper, IOptions<SynapseAspNetCoreOptions> options)
    {
        _invoker = invoker;
        _failureMapper = failureMapper;
        _challenge = options.Value.AuthenticationChallenge;
    }

    public async ValueTask<IActionResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default) where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        var actionResult = await result.AsActionResultBuilder(_failureMapper);
        return result.IsSuccess ? actionResult : new ChallengingActionResult(actionResult, _challenge);
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

        return new ChallengingActionResult(await result.AsActionResultBuilder(_failureMapper), _challenge);
    }

    public async ValueTask<IActionResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        var actionResult = await result.AsActionResultBuilder(_failureMapper);
        return result.IsSuccess ? actionResult : new ChallengingActionResult(actionResult, _challenge);
    }
}
