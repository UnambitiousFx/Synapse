using System.Collections.Immutable;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Information about events and their handlers in the compilation. Both arrays hold names rendered with
///     <see cref="Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat" />, so they are ready to
///     emit as-is — no further globalization.
/// </summary>
internal readonly struct EventInfo
{
    public EventInfo(ImmutableArray<string> eventTypes, ImmutableArray<string> handlerTypes)
    {
        EventTypes = eventTypes;
        HandlerTypes = handlerTypes;
    }

    public ImmutableArray<string> EventTypes { get; }
    public ImmutableArray<string> HandlerTypes { get; }
}