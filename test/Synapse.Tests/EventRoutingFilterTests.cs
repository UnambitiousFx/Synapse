using JetBrains.Annotations;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests;

[TestSubject(typeof(IEventRoutingFilter))]
public sealed class EventRoutingFilterTests
{
    [Fact]
    public void TenantBasedRoutingFilter_WithPremiumTenant_ReturnsHybridMode()
    {
        // Arrange
        var contextAccessor = CreateContextAccessorWithTenant("premium-tenant");
        var filter = new TenantBasedRoutingFilter(contextAccessor);
        var @event = new EventExample("Test Event");

        // Act
        var result = filter.GetDistributionMode(@event);

        // Assert
        Assert.Equal(DistributionMode.LocalAndExternal, result);
    }

    [Fact]
    public void TenantBasedRoutingFilter_WithEnterpriseTenant_ReturnsExternalOnlyMode()
    {
        // Arrange
        var contextAccessor = CreateContextAccessorWithTenant("enterprise-tenant");
        var filter = new TenantBasedRoutingFilter(contextAccessor);
        var @event = new EventExample("Test Event");

        // Act
        var result = filter.GetDistributionMode(@event);

        // Assert
        Assert.Equal(DistributionMode.External, result);
    }

    [Fact]
    public void TenantBasedRoutingFilter_WithStandardTenant_ReturnsLocalOnlyMode()
    {
        // Arrange
        var contextAccessor = CreateContextAccessorWithTenant("standard-tenant");
        var filter = new TenantBasedRoutingFilter(contextAccessor);
        var @event = new EventExample("Test Event");

        // Act
        var result = filter.GetDistributionMode(@event);

        // Assert
        Assert.Equal(DistributionMode.Local, result);
    }

    [Fact]
    public void TenantBasedRoutingFilter_WithUnknownTenant_ReturnsNull()
    {
        // Arrange
        var contextAccessor = CreateContextAccessorWithTenant("unknown-tenant");
        var filter = new TenantBasedRoutingFilter(contextAccessor);
        var @event = new EventExample("Test Event");

        // Act
        var result = filter.GetDistributionMode(@event);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TenantBasedRoutingFilter_WithNoContext_ReturnsNull()
    {
        // Arrange
        var contextAccessor = Substitute.For<IContextAccessor>();
        contextAccessor.Context.Returns((IContext?)null);
        var filter = new TenantBasedRoutingFilter(contextAccessor);
        var @event = new EventExample("Test Event");

        // Act
        var result = filter.GetDistributionMode(@event);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void EnvironmentBasedRoutingFilter_WithDevelopmentEnvironment_ReturnsLocalOnlyMode()
    {
        // Arrange
        var filter = new EnvironmentBasedRoutingFilter("Development");
        var @event = new EventExample("Test Event");

        // Act
        var result = filter.GetDistributionMode(@event);

        // Assert
        Assert.Equal(DistributionMode.Local, result);
    }

    [Fact]
    public void EnvironmentBasedRoutingFilter_WithProductionEnvironment_ReturnsNull()
    {
        // Arrange
        var filter = new EnvironmentBasedRoutingFilter("Production");
        var @event = new EventExample("Test Event");

        // Act
        var result = filter.GetDistributionMode(@event);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FilterOrdering_EnvironmentFilterHasLowerOrderThanTenantFilter()
    {
        // Arrange
        var contextAccessor = Substitute.For<IContextAccessor>();
        var environmentFilter = new EnvironmentBasedRoutingFilter("Production");
        var tenantFilter = new TenantBasedRoutingFilter(contextAccessor);

        // Assert - Environment filter should execute first (lower order value)
        Assert.True(environmentFilter.Order < tenantFilter.Order);
        Assert.Equal(50, environmentFilter.Order);
        Assert.Equal(100, tenantFilter.Order);
    }

    [Fact]
    public void MultipleFilters_FirstFilterReturningModeWins()
    {
        // Arrange
        var contextAccessor = CreateContextAccessorWithTenant("premium-tenant");
        var filters = new List<IEventRoutingFilter>
        {
            new EnvironmentBasedRoutingFilter("Development"), // Order 50, returns LocalOnly
            new TenantBasedRoutingFilter(contextAccessor) // Order 100, returns Hybrid
        };

        // Sort by order as the dispatcher would
        var sortedFilters = filters.OrderBy(f => f.Order).ToList();
        var @event = new EventExample("Test Event");

        // Act - Simulate dispatcher behavior: first non-null result wins
        DistributionMode? result = null;
        foreach (var filter in sortedFilters)
        {
            result = filter.GetDistributionMode(@event);
            if (result.HasValue)
                break;
        }

        // Assert - Environment filter executes first and returns LocalOnly
        Assert.Equal(DistributionMode.Local, result);
    }

    [Fact]
    public void MultipleFilters_WhenFirstReturnsNull_SecondFilterIsEvaluated()
    {
        // Arrange
        var contextAccessor = CreateContextAccessorWithTenant("premium-tenant");
        var filters = new List<IEventRoutingFilter>
        {
            new EnvironmentBasedRoutingFilter("Production"), // Order 50, returns null
            new TenantBasedRoutingFilter(contextAccessor) // Order 100, returns Hybrid
        };

        // Sort by order as the dispatcher would
        var sortedFilters = filters.OrderBy(f => f.Order).ToList();
        var @event = new EventExample("Test Event");

        // Act - Simulate dispatcher behavior: first non-null result wins
        DistributionMode? result = null;
        foreach (var filter in sortedFilters)
        {
            result = filter.GetDistributionMode(@event);
            if (result.HasValue)
                break;
        }

        // Assert - Tenant filter executes second and returns Hybrid
        Assert.Equal(DistributionMode.LocalAndExternal, result);
    }

    [Fact]
    public void MultipleFilters_WhenAllReturnNull_ResultIsNull()
    {
        // Arrange
        var contextAccessor = CreateContextAccessorWithTenant("unknown-tenant");
        var filters = new List<IEventRoutingFilter>
        {
            new EnvironmentBasedRoutingFilter("Production"), // Order 50, returns null
            new TenantBasedRoutingFilter(contextAccessor) // Order 100, returns null
        };

        // Sort by order as the dispatcher would
        var sortedFilters = filters.OrderBy(f => f.Order).ToList();
        var @event = new EventExample("Test Event");

        // Act - Simulate dispatcher behavior: first non-null result wins
        DistributionMode? result = null;
        foreach (var filter in sortedFilters)
        {
            result = filter.GetDistributionMode(@event);
            if (result.HasValue)
                break;
        }

        // Assert - No filter returned a mode, should fall back to default
        Assert.Null(result);
    }

    private static IContextAccessor CreateContextAccessorWithTenant(string tenantId)
    {
        var context = Substitute.For<IContext>();
        context.GetMetadata<string>("TenantId").Returns(tenantId);

        var contextAccessor = Substitute.For<IContextAccessor>();
        contextAccessor.Context.Returns(context);

        return contextAccessor;
    }
}