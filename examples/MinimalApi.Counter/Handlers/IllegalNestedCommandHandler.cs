using UnambitiousFx.Examples.MinimalApi.Counter.Messages;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter.Handlers;

[RequestHandler<IllegalNestedCommand, int>]
public sealed class IllegalNestedCommandHandler : IRequestHandler<IllegalNestedCommand, int>
{
    private readonly IInvoker _invoker;

    public IllegalNestedCommandHandler(IInvoker invoker)
    {
        _invoker = invoker;
    }

    public async ValueTask<Result<int>> HandleAsync(IllegalNestedCommand request, CancellationToken cancellationToken = default)
    {
        // Sending another request from within a handler crosses the CQRS boundary. With enforcement enabled
        // (closed CqrsBoundaryEnforcementBehavior registrations), this throws CqrsBoundaryViolationException.
        return await _invoker.InvokeAsync(new GetCounterQuery(), cancellationToken);
    }
}