using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     A cache-friendly, value-equatable snapshot of a <see cref="Location" />. Storing a raw
///     <see cref="Location" /> in incremental-generator state defeats caching because it is not
///     structurally comparable; this type holds only the file path and spans, which are value types.
/// </summary>
public readonly record struct LocationInfo
{
    public LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
    {
        FilePath = filePath;
        TextSpan = textSpan;
        LineSpan = lineSpan;
    }

    public string FilePath { get; }
    public TextSpan TextSpan { get; }
    public LinePositionSpan LineSpan { get; }

    /// <summary>Rebuilds a <see cref="Location" /> for diagnostic reporting.</summary>
    public Location ToLocation()
    {
        return Location.Create(FilePath, TextSpan, LineSpan);
    }

    /// <summary>Captures a value-equatable snapshot of <paramref name="location" />, or null if it has no source.</summary>
    public static LocationInfo? CreateFrom(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new LocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }
}
