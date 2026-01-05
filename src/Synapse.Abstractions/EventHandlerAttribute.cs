namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents an attribute that marks a class as an event handler for a specific event type.
/// </summary>
/// <typeparam name="TEvent">
///     Specifies the type of event that the marked class handles. The event type must implement the <see cref="IEvent" />
///     interface.
/// </typeparam>
/// <remarks>
///     This attribute is used to decorate classes that are responsible for handling events within the system. Classes
///     using this attribute are typically responsible for encapsulating logic that responds to a specific type of event.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EventHandlerAttribute<TEvent> : Attribute
    where TEvent : IEvent
{
}