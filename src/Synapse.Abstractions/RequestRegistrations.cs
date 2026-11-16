using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Registration of <see cref="IRequestRegistrationVisitor" />s.
/// </summary>
public static class RequestRegistrationServiceCollectionExtensions
{
    /// <summary>
    ///     Adds a visitor that receives every request type Synapse registers, whether the call
    ///     comes before or after <c>AddSynapse</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="visitor">The visitor to notify.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRequestRegistrationVisitor(this IServiceCollection services,
        IRequestRegistrationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        GetOrAdd(services).AddVisitor(visitor);
        return services;
    }

    // The key identifies one handler shape (the closed handler service type), not the request type: a request type
    // can have handlers on several dispatch paths, and each path must be visited.
    internal static void RecordRequestRegistrations(this IServiceCollection services,
        IEnumerable<KeyValuePair<Type, Action<IRequestRegistrationVisitor>>> visits)
    {
        var hub = GetOrAdd(services);
        foreach (var (registrationKey, visit) in visits)
        {
            hub.Record(registrationKey, visit);
        }
    }

    private static RequestRegistrations GetOrAdd(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(RequestRegistrations) &&
                descriptor.ImplementationInstance is RequestRegistrations existing)
            {
                return existing;
            }
        }

        var hub = new RequestRegistrations();
        services.AddSingleton(hub);
        return hub;
    }
}

internal sealed class RequestRegistrations
{
    private readonly Dictionary<Type, Action<IRequestRegistrationVisitor>> _visits = new();
    private readonly List<IRequestRegistrationVisitor> _visitors = new();

    public void Record(Type registrationKey, Action<IRequestRegistrationVisitor> visit)
    {
        if (!_visits.TryAdd(registrationKey, visit))
        {
            return;
        }

        foreach (var visitor in _visitors)
        {
            visit(visitor);
        }
    }

    public void AddVisitor(IRequestRegistrationVisitor visitor)
    {
        _visitors.Add(visitor);
        foreach (var visit in _visits.Values)
        {
            visit(visitor);
        }
    }
}
