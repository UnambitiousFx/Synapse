using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.ModelContextProtocol;

internal sealed class McpInvoker : IMcpInvoker
{
    internal const string VoidSuccessText = "Done.";

    private readonly IMcpFailurePolicy _failurePolicy;
    private readonly IInvoker _invoker;
    private readonly int? _maxAnswerLength;
    private readonly JsonSerializerOptions _serializerOptions;

    public McpInvoker(IInvoker invoker,
        IMcpFailurePolicy failurePolicy,
        IOptions<SynapseMcpOptions> options)
    {
        _invoker = invoker;
        _failurePolicy = failurePolicy;
        _maxAnswerLength = options.Value.MaxAnswerLength;
        _serializerOptions = options.Value.JsonContext?.Options ?? McpJsonUtilities.DefaultOptions;
    }

    public async ValueTask<CallToolResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default) where TResponse : notnull
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        if (!result.TryGet(out var value, out var failure))
        {
            return _failurePolicy.ToToolError(failure!);
        }

        return Answer(JsonSerializer.Serialize(value, GetTypeInfo<TResponse>()));
    }

    public async ValueTask<CallToolResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        var result = await _invoker.InvokeAsync(request, cancellationToken);
        if (result.TryGetFailure(out var failure))
        {
            return _failurePolicy.ToToolError(failure!);
        }

        return new CallToolResult { Content = [new TextContentBlock { Text = VoidSuccessText }] };
    }

    private JsonTypeInfo<TResponse> GetTypeInfo<TResponse>()
    {
        // GetTypeInfo goes through the configured resolver only, so with a source-generated context nothing here
        // needs reflection, which keeps the package NativeAOT-safe.
        if (_serializerOptions.TryGetTypeInfo(typeof(TResponse), out var typeInfo) &&
            typeInfo is JsonTypeInfo<TResponse> typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"No JSON contract for '{typeof(TResponse)}'. Add [JsonSerializable(typeof({typeof(TResponse).Name}))] " +
            $"to the context set in {nameof(SynapseMcpOptions)}.{nameof(SynapseMcpOptions.JsonContext)}.");
    }

    private CallToolResult Answer(string json)
    {
        if (_maxAnswerLength is not { } max ||
            json.Length <= max)
        {
            return new CallToolResult { Content = [new TextContentBlock { Text = json }] };
        }

        var cut = max;
        // Cutting between the two halves of a surrogate pair would leave an invalid character at the end.
        if (char.IsHighSurrogate(json[cut - 1]))
        {
            cut--;
        }

        var notice = string.Create(CultureInfo.InvariantCulture,
            $"[Answer truncated: showing the first {cut} of {json.Length} UTF-16 code units. The JSON above is incomplete; narrow the request to get the rest.]");

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = json[..cut] },
                new TextContentBlock { Text = notice }
            ],
            Meta = new JsonObject
            {
                [SynapseMcpMeta.Truncated] = true,
                [SynapseMcpMeta.OriginalLength] = json.Length
            }
        };
    }
}
