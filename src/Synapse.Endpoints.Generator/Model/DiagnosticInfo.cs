using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     Equatable description of one diagnostic to report, deferred from analysis time (where the
///     descriptor and message arguments are known) to source-output time, where
///     <see cref="SourceProductionContext.ReportDiagnostic" /> is actually available.
/// </summary>
/// <remarks>
///     A raw <see cref="Diagnostic" /> is not carried through pipeline state directly because its
///     <see cref="Location" /> is not structurally comparable, which would defeat incremental
///     caching the same way a raw <see cref="Location" /> would — see <see cref="LocationInfo" />,
///     which this type also relies on for the same reason. Declared with an explicit body rather
///     than positional-record syntax, matching every other pipeline-state type in this project (see
///     <see cref="EndpointTarget" /> for why).
/// </remarks>
internal readonly record struct DiagnosticInfo
{
    public DiagnosticInfo(DiagnosticDescriptor descriptor,
        LocationInfo? location,
        EquatableArray<string> messageArgs)
    {
        Descriptor = descriptor;
        Location = location;
        MessageArgs = messageArgs;
    }

    /// <summary>Which diagnostic this is.</summary>
    public DiagnosticDescriptor Descriptor { get; }

    /// <summary>Where to anchor it.</summary>
    public LocationInfo? Location { get; }

    /// <summary>Arguments substituted into the descriptor's message format.</summary>
    public EquatableArray<string> MessageArgs { get; }

    /// <summary>Materializes the actual <see cref="Diagnostic" /> to report.</summary>
    public Diagnostic ToDiagnostic()
    {
        var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
        return Diagnostic.Create(Descriptor, location, MessageArgs.AsSpan().ToArray());
    }
}
