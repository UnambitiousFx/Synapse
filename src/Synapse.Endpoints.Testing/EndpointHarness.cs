using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.Endpoints.Testing.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Testing;

/// <summary>
///     Creates a harness that runs one endpoint through the real routing stack, without a host.
/// </summary>
/// <remarks>
///     <para>
///         An endpoint's binder and resolved configuration are created when it is mapped, so calling
///         <c>HandleAsync</c> or <c>BindAsync</c> on a new instance throws by design. This type maps the
///         endpoint through the library's own <c>MapEndpoint&lt;TEndpoint&gt;</c> and hands back
///         something that answers requests, which is the supported way to exercise one endpoint in
///         isolation.
///     </para>
///     <para>
///         The endpoint's route comes from its own registered metadata, so a request addresses it by
///         URL — <c>harness.Get($"/tasks/{id}")</c> — and route constraints, group prefixes and verb
///         matching all apply as they do in an application.
///     </para>
/// </remarks>
public static class EndpointHarness
{
    /// <summary>Creates a harness for an endpoint that needs no extra services.</summary>
    /// <typeparam name="TEndpoint">The endpoint type under test.</typeparam>
    /// <returns>The harness. Dispose it when the test finishes.</returns>
    public static EndpointHarness<TEndpoint> Create<TEndpoint>()
        where TEndpoint : EndpointBase, new()
    {
        return Create<TEndpoint>(static _ => { });
    }

    /// <summary>Creates a harness, configuring its services and stubbed handlers first.</summary>
    /// <typeparam name="TEndpoint">The endpoint type under test.</typeparam>
    /// <param name="configure">Configures the harness.</param>
    /// <returns>The harness. Dispose it when the test finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public static EndpointHarness<TEndpoint> Create<TEndpoint>(Action<EndpointHarnessOptions> configure)
        where TEndpoint : EndpointBase, new()
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EndpointHarnessOptions();

        // Seeded before the caller runs so a test can override any of it with its own registration.
        options.Services.AddOptions();
        options.Services.AddLogging();
        options.Services.AddRouting();
        options.Services.AddSynapseAspNetCore();

        // A host registers this from the generic host's DiagnosticListener; nothing here plays that
        // role, so EndpointRoutingMiddleware's constructor injection of DiagnosticListener (and the
        // DiagnosticSource base type some middleware asks for instead) is otherwise unresolved.
        var diagnosticListener = new DiagnosticListener("UnambitiousFx.Synapse.Endpoints.Testing");
        options.Services.AddSingleton(diagnosticListener);
        options.Services.AddSingleton<DiagnosticSource>(diagnosticListener);

        configure(options);

        // After the caller so the stub is the invoker even when a test also touched Services, and
        // singleton because the stub holds only the registrations made above.
        options.Services.AddSingleton<IInvoker>(options.Invoker);

        var (provider, pipeline, routeDescription) = HarnessPipeline.Build<TEndpoint>(options.Services);
        return new EndpointHarness<TEndpoint>(provider, pipeline, routeDescription);
    }
}
