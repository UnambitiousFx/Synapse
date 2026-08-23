using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>Writes a streamed sequence to the response in one of two negotiated formats.</summary>
internal static class StreamResponseWriter
{
    private static readonly byte[] SseDataPrefix = Encoding.UTF8.GetBytes("data: ");
    private static readonly byte[] SseTerminator = Encoding.UTF8.GetBytes("\n\n");

    /// <summary>Writes the sequence as a JSON array, flushing as items arrive.</summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="items">The sequence.</param>
    /// <param name="typeInfo">The item's JSON type info.</param>
    /// <returns>A task representing the write.</returns>
    internal static async Task WriteJsonArrayAsync<TItem>(HttpContext context,
        IAsyncEnumerable<TItem> items,
        JsonTypeInfo<TItem> typeInfo)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = context.Response.BodyWriter;
        var first = true;

        WriteBytes(body, "["u8);

        await foreach (var item in items.WithCancellation(context.RequestAborted))
        {
            if (!first)
            {
                WriteBytes(body, ","u8);
            }

            first = false;

            var utf8 = JsonSerializer.SerializeToUtf8Bytes(item, typeInfo);
            WriteBytes(body, utf8);
            await body.FlushAsync(context.RequestAborted);
        }

        WriteBytes(body, "]"u8);
        await body.FlushAsync(context.RequestAborted);
    }

    /// <summary>Writes the sequence as server-sent events.</summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="items">The sequence.</param>
    /// <param name="typeInfo">The item's JSON type info.</param>
    /// <returns>A task representing the write.</returns>
    internal static async Task WriteServerSentEventsAsync<TItem>(HttpContext context,
        IAsyncEnumerable<TItem> items,
        JsonTypeInfo<TItem> typeInfo)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        var body = context.Response.BodyWriter;

        await foreach (var item in items.WithCancellation(context.RequestAborted))
        {
            WriteBytes(body, SseDataPrefix);
            WriteBytes(body, JsonSerializer.SerializeToUtf8Bytes(item, typeInfo));
            WriteBytes(body, SseTerminator);
            await body.FlushAsync(context.RequestAborted);
        }
    }

    /// <summary>
    ///     Copies a byte sequence into the writer's buffer. <see cref="PipeWriter" /> exposes no
    ///     synchronous span-based write helper of its own, so bytes are staged via
    ///     <see cref="PipeWriter.GetSpan" /> and committed with <see cref="PipeWriter.Advance" />.
    /// </summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="bytes">The bytes to copy.</param>
    private static void WriteBytes(PipeWriter writer,
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        var span = writer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        writer.Advance(bytes.Length);
    }
}
