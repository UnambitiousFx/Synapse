using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Options for <see cref="ServiceCollectionExtensions.AddSynapseAuthorization" />.
/// </summary>
public sealed class SynapseAuthorizationOptions
{
    /// <summary>The default <see cref="QueryScope" />.</summary>
    public const string DefaultQueryScope = "read";

    /// <summary>The default <see cref="CommandScope" />.</summary>
    public const string DefaultCommandScope = "write";

    private readonly List<Action<IServiceCollection>> _registrations = [];

    /// <summary>
    ///     The scope every query (<c>IQuery&lt;T&gt;</c>) requires unless it implements <see cref="IRequiresScope" />.
    ///     <see langword="null" /> turns the default off for queries. Defaults to <see cref="DefaultQueryScope" />.
    /// </summary>
    public string? QueryScope { get; set; } = DefaultQueryScope;

    /// <summary>
    ///     The scope every command (<c>ICommand</c>, <c>ICommand&lt;T&gt;</c>) requires unless it implements
    ///     <see cref="IRequiresScope" />. <see langword="null" /> turns the default off for commands. Defaults to
    ///     <see cref="DefaultCommandScope" />.
    /// </summary>
    public string? CommandScope { get; set; } = DefaultCommandScope;

    /// <summary>
    ///     Turns the default scope rule off: only requests implementing <see cref="IRequiresScope" /> are checked for a
    ///     scope. Same as setting <see cref="QueryScope" /> and <see cref="CommandScope" /> to <see langword="null" />.
    /// </summary>
    /// <returns>The same options, for chaining.</returns>
    public SynapseAuthorizationOptions DisableDefaultScopes()
    {
        QueryScope = null;
        CommandScope = null;
        return this;
    }

    /// <summary>
    ///     Registers the application's <see cref="IResourceAccessPolicy" /> as a scoped service, replacing any policy
    ///     already registered.
    /// </summary>
    /// <typeparam name="TPolicy">The policy type.</typeparam>
    /// <returns>The same options, for chaining.</returns>
    public SynapseAuthorizationOptions Policy<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TPolicy>()
        where TPolicy : class, IResourceAccessPolicy
    {
        _registrations.Add(services =>
            services.Replace(ServiceDescriptor.Scoped<IResourceAccessPolicy, TPolicy>()));
        return this;
    }

    /// <summary>
    ///     Adds an <see cref="IAuthorizationDenialRecorder" />, registered as a scoped service. Every registered
    ///     recorder is called for each denial.
    /// </summary>
    /// <typeparam name="TRecorder">The recorder type.</typeparam>
    /// <returns>The same options, for chaining.</returns>
    public SynapseAuthorizationOptions DenialRecorder<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRecorder>()
        where TRecorder : class, IAuthorizationDenialRecorder
    {
        _registrations.Add(services =>
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationDenialRecorder, TRecorder>()));
        return this;
    }

    internal void ApplyRegistrations(IServiceCollection services)
    {
        foreach (var registration in _registrations)
        {
            registration(services);
        }
    }
}
