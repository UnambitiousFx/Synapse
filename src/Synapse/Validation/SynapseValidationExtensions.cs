using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Validation;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Checks a Synapse configuration for mistakes that otherwise only show up at the first request.
/// </summary>
public static class SynapseValidationExtensions
{
    /// <summary>
    ///     Validates what <c>AddSynapse</c> registered: a behavior registered for a request or event without a
    ///     handler (SYN001), several handlers for one request (SYN002), a pipeline that cannot be resolved
    ///     (SYN003), and behaviors sharing an <c>Order</c> (SYN004, a warning).
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <returns>The report. It is never thrown; call <see cref="SynapseValidationReport.ThrowIfInvalid" /> to fail.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><c>AddSynapse</c> was never called on this provider's collection.</exception>
    /// <remarks>
    ///     Resolves every registered handler and its behaviors once, like <see cref="IPipelineDescriber" />, so a
    ///     handler whose constructor needs something that only exists inside a real request reports SYN003.
    ///     Behaviors added to the service collection after <c>AddSynapse</c> returned are not seen, and open-generic
    ///     behaviors are not checked.
    /// </remarks>
    public static SynapseValidationReport ValidateSynapse(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetService<SynapseRegistry>() ??
                       throw new InvalidOperationException(
                           "Synapse is not registered. Call AddSynapse on the service collection first.");
        return SynapseValidator.Validate(registry, services.GetRequiredService<IPipelineDescriber>());
    }
}
