namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Provides custom routing logic to determine how events should be distributed.
///     Routing filters are evaluated in order of their <see cref="Order" /> property,
///     allowing dynamic distribution decisions based on runtime conditions.
/// </summary>
/// <remarks>
///     <para>
///         Routing filters enable flexible event routing strategies such as:
///         <list type="bullet">
///             <item>Tenant-based routing (e.g., premium tenants get hybrid distribution)</item>
///             <item>Environment-based routing (e.g., development uses local-only)</item>
///             <item>Feature flag-based routing (e.g., enable external dispatch for specific features)</item>
///             <item>Load-based routing (e.g., defer external dispatch during high load)</item>
///         </list>
///     </para>
///     <para>
///         When multiple filters are registered, they execute in ascending order of <see cref="Order" />.
///         The first filter that returns a non-null <see cref="DistributionMode" /> determines the routing.
///         If no filter returns a mode, the system falls back to message traits or default configuration.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public class TenantBasedRoutingFilter : IEventRoutingFilter
///     {
///         private readonly IContextAccessor _contextAccessor;
///         
///         public int Order => 100;
///         
///         public DistributionMode? GetDistributionMode&lt;TEvent&gt;(TEvent @event) 
///             where TEvent : class, IEvent
///         {
///             var tenantId = _contextAccessor.Context?.GetMetadata&lt;string&gt;("TenantId");
///             
///             // Route premium tenant events to both local and external handlers
///             if (tenantId == "premium-tenant")
///                 return DistributionMode.Hybrid;
///             
///             // Defer to next filter or default configuration
///             return null;
///         }
///     }
///     </code>
/// </example>
public interface IEventRoutingFilter
{
    /// <summary>
    ///     Gets the priority order for this filter. Lower values execute first.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Filters are executed in ascending order of this value. For example:
    ///         <list type="bullet">
    ///             <item>Order = 0: Highest priority, executes first</item>
    ///             <item>Order = 100: Medium priority</item>
    ///             <item>Order = 1000: Lower priority, executes later</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         Use lower order values for filters that should take precedence over others,
    ///         such as security or compliance-related routing decisions.
    ///     </para>
    /// </remarks>
    int Order { get; }

    /// <summary>
    ///     Determines the distribution mode for the given event.
    /// </summary>
    /// <typeparam name="TEvent">The type of event being routed.</typeparam>
    /// <param name="event">The event instance to evaluate for routing.</param>
    /// <returns>
    ///     The <see cref="DistributionMode" /> if this filter handles the event,
    ///     or <c>null</c> to defer to the next filter or default configuration.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Implementations should return <c>null</c> when they don't have a specific
    ///         routing decision for the event, allowing other filters or the default
    ///         configuration to determine the distribution mode.
    ///     </para>
    ///     <para>
    ///         This method should be fast and avoid expensive operations, as it's called
    ///         for every event dispatch. Consider caching routing decisions when appropriate.
    ///     </para>
    /// </remarks>
    DistributionMode? GetDistributionMode<TEvent>(TEvent @event) where TEvent : class, IEvent;
}