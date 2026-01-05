using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>
///     Example routing filter that demonstrates environment-based event routing.
///     This filter has higher priority (lower order) than TenantBasedRoutingFilter.
/// </summary>
public sealed class EnvironmentBasedRoutingFilter : IEventRoutingFilter
{
    private readonly string _environment;

    public EnvironmentBasedRoutingFilter(string environment)
    {
        _environment = environment;
    }

    /// <summary>
    ///     Gets the filter order. Lower values execute first.
    ///     This filter has priority 50, so it executes before TenantBasedRoutingFilter (100).
    /// </summary>
    public int Order => 50;

    /// <summary>
    ///     Determines distribution mode based on environment.
    /// </summary>
    public DistributionMode? GetDistributionMode<TEvent>(TEvent @event) where TEvent : class, IEvent
    {
        // In development, always use local-only to avoid external dependencies
        if (_environment == "Development")
            return DistributionMode.Local;

        // In production, defer to other filters or default configuration
        return null;
    }
}