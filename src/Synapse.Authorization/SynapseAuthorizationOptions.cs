using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Options for <see cref="ServiceCollectionExtensions.AddSynapseAuthorization" />. One instance is shared by every
///     call to it, so each call's settings apply.
/// </summary>
public sealed class SynapseAuthorizationOptions
{
    /// <summary>The default <see cref="QueryScope" />.</summary>
    public const string DefaultQueryScope = "read";

    /// <summary>The default <see cref="CommandScope" />.</summary>
    public const string DefaultCommandScope = "write";

    private readonly Dictionary<Type, RequestIntent> _intents = [];
    private readonly List<Action<IServiceCollection>> _registrations = [];

    /// <summary>
    ///     The scope every query (<c>IQuery&lt;T&gt;</c>) requires unless it implements <see cref="IRequiresScope" />.
    ///     <see langword="null" /> turns the default off for queries. Defaults to <see cref="DefaultQueryScope" />.
    ///     The marker is read through the handler shapes registered for the request: the <c>IQuery&lt;T&gt;</c> whose <c>T</c> is
    ///     the handler's response type. When a type has several handler shapes, the strictest intent applies
    ///     (command over query).
    /// </summary>
    public string? QueryScope { get; set; } = DefaultQueryScope;

    /// <summary>
    ///     The scope every command (<c>ICommand</c>, <c>ICommand&lt;T&gt;</c>) requires unless it implements
    ///     <see cref="IRequiresScope" />. <see langword="null" /> turns the default off for commands. Defaults to
    ///     <see cref="DefaultCommandScope" />.
    ///     The marker is read through the handler shapes registered for the request: the <c>ICommand&lt;T&gt;</c> whose <c>T</c> is
    ///     the handler's response type, or the non-generic <c>ICommand</c>. When a type has several handler shapes, the strictest intent applies
    ///     (command over query).
    /// </summary>
    public string? CommandScope { get; set; } = DefaultCommandScope;

    /// <summary>
    ///     Shapes the failure returned for a denial, for example to give it a domain <c>Code</c> and a message the
    ///     application shows or translates. Called with the <see cref="AuthorizationDenial" /> the recorders receive;
    ///     return <see langword="null" /> to keep the default failure. <see langword="null" /> (the default) keeps every
    ///     default failure.
    /// </summary>
    /// <remarks>
    ///     The failure's type cannot change: return an <see cref="UnauthenticatedFailure" /> for
    ///     <see cref="AuthorizationDenialReason.Unauthenticated" />, a <see cref="NotFoundFailure" /> for
    ///     <see cref="AuthorizationDenialReason.ResourceHidden" /> and an <see cref="UnauthorizedFailure" /> otherwise
    ///     (or a subclass). Transports map the type to 401 / 404 / 403, so another type throws an
    ///     <see cref="InvalidOperationException" /> rather than turn a denial into another status. Only the resource
    ///     policy decides whether a denial hides the resource (<see cref="ResourceAccess.Hidden" />).
    /// </remarks>
    public Func<AuthorizationDenial, Failure?>? DenialFailure { get; set; }

    /// <summary>
    ///     Which checks a principal whose <see cref="ICurrentPrincipal.IsSystem" /> is <see langword="true" /> skips,
    ///     such as <see cref="SystemPrincipal.Instance" />. Defaults to <see cref="Authorization.SystemBypass.All" />.
    /// </summary>
    public SystemBypass SystemBypass { get; set; } = SystemBypass.All;

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
    ///     Registers the <see cref="IRequestAccessPolicy{TRequest}" /> of <typeparamref name="TRequest" /> as a scoped
    ///     service, replacing any policy already registered for that request type.
    /// </summary>
    /// <typeparam name="TRequest">The request type the policy authorizes.</typeparam>
    /// <typeparam name="TPolicy">The policy type.</typeparam>
    /// <returns>The same options, for chaining.</returns>
    public SynapseAuthorizationOptions RequestPolicy<TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TPolicy>()
        where TPolicy : class, IRequestAccessPolicy<TRequest>
    {
        _registrations.Add(services =>
            services.Replace(ServiceDescriptor.Scoped<IRequestAccessPolicy<TRequest>, TPolicy>()));
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

    // Recorded by AuthorizationRegistrationVisitor, which sees the closed types core registers: a reflection-free
    // replacement for scanning the interfaces, which native AOT cannot do. Kept per options instance, so one container
    // never changes another's scopes. Writes happen during service registration (single-threaded per collection) and
    // reads after the provider is built, so no locking. A type with several handler shapes keeps the strictest intent
    // (Command > Query > Other), independent of registration order. Limit: a marker with no registered shape is not seen.
    internal void RecordIntent(Type request, RequestIntent intent)
    {
        if (!_intents.TryGetValue(request, out var current) || intent == RequestIntent.Command ||
            (intent == RequestIntent.Query && current == RequestIntent.Other))
        {
            _intents[request] = intent;
        }
    }

    internal RequestIntent IntentOf(Type request)
    {
        // A wiring error surfaced when the behavior is constructed, not a request outcome, so it throws instead of
        // returning an UnauthorizedFailure. Unknown fails closed: the request would silently lose its default scope.
        return _intents.TryGetValue(request, out var intent)
            ? intent
            : throw new InvalidOperationException(
                $"The request type '{request}' was not registered through Synapse, so its authorization intent is unknown (or AddSynapseAuthorization was not called on the same service collection).");
    }

    internal void Validate()
    {
        ThrowIfBlank(QueryScope, nameof(QueryScope));
        ThrowIfBlank(CommandScope, nameof(CommandScope));
        if (!Enum.IsDefined(SystemBypass))
        {
            throw new ArgumentException(
                $"{nameof(SynapseAuthorizationOptions)}.{nameof(SystemBypass)} is not a {nameof(Authorization.SystemBypass)} value.",
                nameof(SystemBypass));
        }
    }

    // The instance is shared by every AddSynapseAuthorization call, so each call applies only what it added.
    internal void ApplyRegistrations(IServiceCollection services)
    {
        foreach (var registration in _registrations)
        {
            registration(services);
        }

        _registrations.Clear();
    }

    private static void ThrowIfBlank(string? scope, string name)
    {
        // An empty scope would be a requirement no caller can meet; null is the explicit way to turn it off.
        if (scope is not null && string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException(
                $"{nameof(SynapseAuthorizationOptions)}.{name} is empty or whitespace. Set it to null to turn the default scope off.",
                name);
        }
    }
}
