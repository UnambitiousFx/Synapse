using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.FluentValidation;

// Closes the validation behavior over each registered request type. Closed descriptors stay visible to the trimmer
// and native AOT, which cannot close open generics at runtime. Scoped, like the validators AddValidatorsFrom*
// registers by default.
internal sealed class FluentValidationRegistrationVisitor(IServiceCollection services) : IRequestRegistrationVisitor
{
    public void VisitRequest<TRequest>() where TRequest : IRequest
    {
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IRequestPipelineBehavior<TRequest>, FluentValidationBehavior<TRequest>>());
    }

    public void VisitRequest<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IRequestPipelineBehavior<TRequest, TResponse>, FluentValidationBehavior<TRequest, TResponse>>());
    }

    public void VisitStreamRequest<TRequest, TItem>()
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        // No stream validation behavior exists.
    }
}
