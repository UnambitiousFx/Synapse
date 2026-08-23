namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     The full analysis outcome for one candidate class declaration that matched an endpoint base
///     type: every diagnostic found, plus the <see cref="EndpointTarget" /> to emit — or
///     <see langword="null" /> when any diagnostic found is error-severity, so that a broken endpoint
///     does not also produce uncompilable generated code (a missing parameterless constructor, for
///     example, would otherwise surface again — more confusingly — as a constraint error on the
///     generated <c>MapEndpoint&lt;TEndpoint&gt;()</c> call).
/// </summary>
internal readonly record struct EndpointAnalysisResult
{
    public EndpointAnalysisResult(EndpointTarget? target,
        EquatableArray<DiagnosticInfo> diagnostics)
    {
        Target = target;
        Diagnostics = diagnostics;
    }

    /// <summary>The endpoint to emit, or null when an error-severity diagnostic was reported for it.</summary>
    public EndpointTarget? Target { get; }

    /// <summary>Every diagnostic found for this candidate, reported regardless of <see cref="Target" />.</summary>
    public EquatableArray<DiagnosticInfo> Diagnostics { get; }
}
