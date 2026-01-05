using System.Collections.Immutable;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Information about events and their handlers in the compilation.
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