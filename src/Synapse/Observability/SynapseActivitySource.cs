using System.Diagnostics;

namespace UnambitiousFx.Synapse.Observability;

/// <summary>
///     Provides the ActivitySource for OpenTelemetry distributed tracing in the mediator transport system.
/// </summary>
public static class SynapseActivitySource
{
    /// <summary>
    ///     The ActivitySource for creating activity spans for distributed tracing.
    /// </summary>
    public static readonly ActivitySource Source = new("Unambitious.Synapse", "1.0.0");
}