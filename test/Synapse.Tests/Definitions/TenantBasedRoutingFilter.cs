using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>
///     Example routing filter that demonstrates tenant-based event routing.
///     Routes events differently based on tenant context.
/// </summary>
public sealed class TenantBasedRoutingFilter : IEventRoutingFilter
{
    private readonly IContextAccessor _contextAccessor;

    public TenantBasedRoutingFilter(IContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <summary>
    ///     Gets the filter order. Lower values execute first.
    /// </summary>
    public int Order => 100;

    /// <summary>
    ///     Determines distribution mode based on tenant context.
    /// </summary>
    public DistributionMode? GetDistributionMode<TEvent>(TEvent @event) where TEvent : class, IEvent
    {
        var context = _contextAccessor.Context;
        if (context == null)
            return null;

        var tenantId = context.GetMetadata<string>("TenantId");

        // Route premium tenant events to both local and external handlers
        if (tenantId == "premium-tenant")
            return DistributionMode.LocalAndExternal;

        // Route enterprise tenant events to external only
        if (tenantId == "enterprise-tenant")
            return DistributionMode.External;

        // Standard tenants use local-only by default
        if (tenantId == "standard-tenant")
            return DistributionMode.Local;

        // Defer to next filter or default configuration
        return null;
    }
}