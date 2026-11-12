using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     An <see cref="IPropagationCarrier" /> over the <c>_meta</c> object of an MCP request.
/// </summary>
/// <remarks>
///     <para>
///         MCP has no headers; the place for out-of-band request data is <c>params._meta</c>, which is where MCP
///         clients put W3C <c>traceparent</c>, <c>tracestate</c> and <c>baggage</c>. Hand this carrier to
///         <see cref="IContextPropagator.Extract" /> in a call-tool filter and store the result in
///         <see cref="IInboundContextStore" />, and the tool's request continues the client's flow.
///     </para>
///     <para>
///         Only string values are read. A missing <c>_meta</c>, a missing key or a value of another JSON kind is
///         reported as absent.
///     </para>
///     <para>
///         <b>Security:</b> <c>_meta</c> is chosen by the client. Adopt it only from clients you trust with your
///         trace identity; the trace id is a correlation label, never an authorization input.
///     </para>
/// </remarks>
public sealed class McpMetaPropagationCarrier : IPropagationCarrier
{
    private readonly RequestParams _parameters;

    /// <summary>
    ///     Wraps the parameters of an MCP request.
    /// </summary>
    /// <param name="parameters">The request parameters whose <c>_meta</c> holds the propagated state.</param>
    public McpMetaPropagationCarrier(RequestParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _parameters = parameters;
    }

    /// <inheritdoc />
    public bool TryGetValue(string key,
        out string? value)
    {
        value = null;

        if (_parameters.Meta is not { } meta ||
            !meta.TryGetPropertyValue(key, out var node) ||
            node is not JsonValue jsonValue ||
            jsonValue.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        value = jsonValue.GetValue<string>();
        return true;
    }

    /// <inheritdoc />
    public void Set(string key,
        string value)
    {
        var meta = _parameters.Meta ??= new JsonObject();
        meta[key] = value;
    }
}
