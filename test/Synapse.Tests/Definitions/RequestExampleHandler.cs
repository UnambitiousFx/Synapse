using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

public sealed class RequestExampleHandler : IRequestHandler<RequestExample>
{
    public bool Executed { get; private set; }
    public RequestExample? RequestExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ResultTask HandleAsync(RequestExample request,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        RequestExecuted = request;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return new ResultTask(Result.Success());
    }
}