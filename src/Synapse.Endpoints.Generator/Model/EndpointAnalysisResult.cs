namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     The full analysis outcome for one candidate class declaration that matched an endpoint base
///     type: every diagnostic found, plus the <see cref="EndpointTarget" /> to emit — or
///     <see langword="null" /> when an error-severity diagnostic found for it would otherwise be
///     blocking, so that a broken endpoint does not also produce uncompilable generated code (a
///     missing parameterless constructor, for example, would otherwise surface again — more
///     confusingly — as a constraint error on the generated <c>MapEndpoint&lt;TEndpoint&gt;()</c>
///     call). SYNE011 and SYNE012 are the exception: both are error-severity, but the property they
///     report is already omitted from <c>EndpointTarget.BoundProperties</c> and the rest of the
///     endpoint still generates working code around that omission, so
///     <c>EndpointsGenerator.Analyze</c> deliberately excludes just those two IDs from the check that
///     nulls <see cref="Target" />.
/// </summary>
internal readonly record struct EndpointAnalysisResult
{
    public EndpointAnalysisResult(EndpointTarget? target,
        EquatableArray<DiagnosticInfo> diagnostics)
    {
        Target = target;
        Diagnostics = diagnostics;
    }

    /// <summary>
    ///     The endpoint to emit, or null when a blocking error-severity diagnostic was reported for
    ///     it (every error-severity ID except SYNE011 and SYNE012 — see the type-level remarks).
    /// </summary>
    public EndpointTarget? Target { get; }

    /// <summary>Every diagnostic found for this candidate, reported regardless of <see cref="Target" />.</summary>
    public EquatableArray<DiagnosticInfo> Diagnostics { get; }
}
