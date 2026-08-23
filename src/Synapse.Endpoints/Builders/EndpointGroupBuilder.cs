using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>Default <see cref="IEndpointGroupBuilder" /> implementation.</summary>
internal sealed class EndpointGroupBuilder : IEndpointGroupBuilder
{
    private readonly List<Action<RouteGroupBuilder>> _configurations = [];
    private string _prefix = string.Empty;

    /// <inheritdoc />
    public IEndpointGroupBuilder Prefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _prefix = prefix;
        return this;
    }

    /// <inheritdoc />
    public IEndpointGroupBuilder Tag(params string[] tags)
    {
        _configurations.Add(group => group.WithTags(tags));
        return this;
    }

    /// <inheritdoc />
    public IEndpointGroupBuilder RequireAuthorization(params string[] policies)
    {
        _configurations.Add(group =>
        {
            if (policies.Length == 0)
            {
                group.RequireAuthorization();
            }
            else
            {
                group.RequireAuthorization(policies);
            }
        });
        return this;
    }

    /// <inheritdoc />
    public IEndpointGroupBuilder Raw(Action<RouteGroupBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configurations.Add(configure);
        return this;
    }

    /// <summary>Gets the route prefix set via <see cref="Prefix" />, or empty when none was set.</summary>
    /// <returns>The route prefix.</returns>
    internal string GetPrefix()
    {
        return _prefix;
    }

    /// <summary>Applies every configuration collected so far to the mapped group.</summary>
    /// <param name="group">The group's route builder.</param>
    internal void Apply(RouteGroupBuilder group)
    {
        foreach (var configuration in _configurations)
        {
            configuration(group);
        }
    }
}
