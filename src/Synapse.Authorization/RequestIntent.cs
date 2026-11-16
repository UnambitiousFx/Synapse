using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

internal enum RequestIntent
{
    Other,
    Command,
    Query
}

// Set once per request type by AuthorizationRegistrationVisitor, which sees the closed types core registers: a
// reflection-free replacement for scanning the interfaces, which native AOT cannot do. Unset fails closed, because a
// request Synapse never registered would otherwise silently lose its default scope.
internal static class RequestIntentOf<TRequest>
{
    private static RequestIntent? _value;

    public static RequestIntent Value => _value ??
                                         throw new InvalidOperationException(
                                             $"The request type '{typeof(TRequest)}' was not registered through Synapse, so its authorization intent is unknown.");

    public static void Set(RequestIntent intent)
    {
        _value = intent;
    }
}
