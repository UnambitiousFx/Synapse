using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

// Closes the authorization behaviors over each registered request type and records its intent. Registering closed
// descriptors keeps them visible to the trimmer and native AOT, which cannot close open generics at runtime.
internal sealed class AuthorizationRegistrationVisitor(IServiceCollection services, SynapseAuthorizationOptions options) : IRequestRegistrationVisitor
{
    public void VisitRequest<TRequest>() where TRequest : IRequest
    {
        // A request marked both ways changes state, so it needs the command scope.
        options.RecordIntent(typeof(TRequest),typeof(ICommand).IsAssignableFrom(typeof(TRequest))
            ? RequestIntent.Command
            : RequestIntent.Other);
        AddResultBehaviors<TRequest, Result>();
    }

    public void VisitRequest<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        options.RecordIntent(typeof(TRequest),
            typeof(ICommand<TResponse>).IsAssignableFrom(typeof(TRequest)) ||
            typeof(ICommand).IsAssignableFrom(typeof(TRequest)) ? RequestIntent.Command
            : typeof(IQuery<TResponse>).IsAssignableFrom(typeof(TRequest)) ? RequestIntent.Query
            : RequestIntent.Other);
        AddResultBehaviors<TRequest, Result<TResponse>>();
    }

    public void VisitStreamRequest<TRequest, TItem>()
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        // A stream only reads, so it needs the query scope unless it is also a command.
        options.RecordIntent(typeof(TRequest),typeof(ICommand).IsAssignableFrom(typeof(TRequest))
            ? RequestIntent.Command
            : RequestIntent.Query);
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IStreamRequestPipelineBehavior<TRequest, TItem>, StreamAuthorizationBehavior<TRequest, TItem>>());
    }

    // Scoped, like the ICurrentPrincipal they read, which is per request / per call.
    private void AddResultBehaviors<TRequest, TResult>() where TResult : IResult
    {
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IResultPipelineBehavior<TRequest, TResult>, ActorAuthorizationBehavior<TRequest, TResult>>());
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IResultPipelineBehavior<TRequest, TResult>, RoleAuthorizationBehavior<TRequest, TResult>>());
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IResultPipelineBehavior<TRequest, TResult>, ScopeAuthorizationBehavior<TRequest, TResult>>());
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IResultPipelineBehavior<TRequest, TResult>,
                RequestPolicyAuthorizationBehavior<TRequest, TResult>>());
        services.TryAddEnumerable(ServiceDescriptor
            .Scoped<IResultPipelineBehavior<TRequest, TResult>, ResourceAuthorizationBehavior<TRequest, TResult>>());
    }
}
