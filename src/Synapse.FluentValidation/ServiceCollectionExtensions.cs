using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.FluentValidation;

/// <summary>
///     Registers the FluentValidation request behaviors.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Validates every request with the <c>IValidator&lt;TRequest&gt;</c> instances registered in the container
    ///     (for example with <c>AddValidatorsFromAssemblyContaining</c>), for both <c>Result</c> and
    ///     <c>Result&lt;T&gt;</c> requests. A failed validation returns a <c>ValidationFailure</c> carrying the field,
    ///     message and code of each error, and the handler is not called.
    /// </summary>
    /// <remarks>
    ///     The behavior is registered closed, for each request type that <c>AddSynapse</c> registers a handler for, so
    ///     call this method and <c>AddSynapse</c> on the same <see cref="IServiceCollection" />, in either order.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optionally sets the behavior's pipeline order.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSynapseFluentValidation(this IServiceCollection services,
        Action<FluentValidationBehaviorOptions>? configure = null)
    {
        var options = new FluentValidationBehaviorOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        // Every call: TryAddEnumerable dedupes the closed behaviors.
        services.AddRequestRegistrationVisitor(new FluentValidationRegistrationVisitor(services));
        return services;
    }
}
