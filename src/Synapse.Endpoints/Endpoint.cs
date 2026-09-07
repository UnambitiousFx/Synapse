using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     An endpoint that dispatches a command with no response. Responds with
///     <c>204 No Content</c> unless configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command, which doubles as the HTTP request contract.</typeparam>
/// <remarks>
///     See <see cref="Endpoint{TRequest,TResponse}" />; this is the same level for the arity with no
///     response body. An empty marker over <see cref="RawEndpoint{TRequest}" />, whose
///     <c>BindAsync</c> the analyzer writes into the endpoint's own <c>partial</c> class — so
///     deriving from this class <em>requires</em> the analyzer rather than merely benefiting from it.
/// </remarks>
public abstract class Endpoint<TRequest> : RawEndpoint<TRequest>
    where TRequest : IRequest;
