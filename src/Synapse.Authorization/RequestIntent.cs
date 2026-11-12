using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

internal enum RequestIntent
{
    Other,
    Command,
    Query
}

// Computed once per request type: ICommand<T> and IQuery<T> are generic, so a behavior that does not know the
// response type cannot pattern-match them per dispatch.
internal static class RequestIntentOf<TRequest>
{
    public static readonly RequestIntent Value = Compute();

    private static RequestIntent Compute()
    {
        var type = typeof(TRequest);
        if (typeof(ICommand).IsAssignableFrom(type))
        {
            return RequestIntent.Command;
        }

        var isQuery = false;
        foreach (var contract in type.GetInterfaces())
        {
            if (!contract.IsGenericType)
            {
                continue;
            }

            var definition = contract.GetGenericTypeDefinition();
            // A request marked both ways changes state, so it needs the command scope.
            if (definition == typeof(ICommand<>))
            {
                return RequestIntent.Command;
            }

            isQuery |= definition == typeof(IQuery<>);
        }

        return isQuery ? RequestIntent.Query : RequestIntent.Other;
    }
}
