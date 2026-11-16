using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

internal enum RequestIntent
{
    Other,
    Command,
    Query
}

// Set per request type by AuthorizationRegistrationVisitor, which sees the closed types core registers: a
// reflection-free replacement for scanning the interfaces, which native AOT cannot do. Unset fails closed, because a
// request Synapse never registered would otherwise silently lose its default scope. Limit: the intent comes from the
// command / query interfaces matching the handler shapes registered for the request; a marker with no registered
// shape is not seen. A type with several shapes keeps the strictest intent (Command > Query > Other), so it does not
// depend on registration order.
internal static class RequestIntentOf<TRequest>
{
    private static RequestIntent? _value;

    public static RequestIntent Value => _value ??
                                         throw new InvalidOperationException(
                                             $"The request type '{typeof(TRequest)}' was not registered through Synapse, so its authorization intent is unknown (or AddSynapseAuthorization was not called on the same service collection).");

    public static void Set(RequestIntent intent)
    {
        if (_value is not { } current || intent == RequestIntent.Command ||
            (intent == RequestIntent.Query && current == RequestIntent.Other))
        {
            _value = intent;
        }
    }
}
