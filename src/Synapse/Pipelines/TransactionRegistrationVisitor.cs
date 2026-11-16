using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

// Closes the transaction behavior over each registered ITransactional request, so the descriptors are visible to the
// trimmer and native AOT. TryAddEnumerable dedups it against the identical closed registration the generator emits.
internal sealed class TransactionRegistrationVisitor(IServiceCollection services) : IRequestRegistrationVisitor
{
    public void VisitRequest<TRequest>() where TRequest : IRequest
    {
        if (ShouldRegister<TRequest>())
        {
            services.RegisterResultPipelineBehavior<TransactionBehavior<TRequest, Result>, TRequest>();
        }
    }

    public void VisitRequest<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        if (ShouldRegister<TRequest>())
        {
            services.RegisterResultPipelineBehavior<TransactionBehavior<TRequest, Result<TResponse>>, TRequest,
                TResponse>();
        }
    }

    public void VisitStreamRequest<TRequest, TItem>()
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        // A stream has no Result to commit on.
    }

    private static bool ShouldRegister<TRequest>()
    {
        return typeof(ITransactional).IsAssignableFrom(typeof(TRequest));
    }
}
