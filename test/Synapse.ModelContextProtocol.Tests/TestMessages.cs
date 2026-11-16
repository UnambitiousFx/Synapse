using System.Text.Json;
using System.Text.Json.Serialization;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.ModelContextProtocol.Tests;

public sealed record Loan(Guid Id, string Label, decimal Amount);

public sealed record GetLoan(Guid Id) : IQuery<Loan>;

public sealed record RenameLoan(Guid Id, string Label) : ICommand<Loan>;

public sealed record DeleteLoan(Guid Id) : ICommand;

public sealed record Unregistered(string Value);

public sealed record GetUnregistered : IQuery<Unregistered>;

public sealed record GetText : IQuery<string>;

public sealed record SetText : ICommand<string>;

[JsonConverter(typeof(RawTextConverter))]
public sealed record RawText(string Value);

public sealed record GetRawText : IQuery<RawText>;

/// <summary>Writes the value as a JSON string without escaping anything.</summary>
public sealed class RawTextConverter : JsonConverter<RawText>
{
    public override RawText Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return new RawText(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer,
        RawText value,
        JsonSerializerOptions options)
    {
        writer.WriteRawValue($"\"{value.Value}\"", skipInputValidation: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Loan))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(RawText))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

/// <summary>Writes a <see cref="Guid" /> in upper case, standing in for an application converter.</summary>
public sealed class UpperCaseGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.GetGuid();
    }

    public override void Write(Utf8JsonWriter writer,
        Guid value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString().ToUpperInvariant());
    }
}

/// <summary>A second context, as a modular application has one per module.</summary>
[JsonSerializable(typeof(Unregistered))]
internal sealed partial class OtherJsonContext : JsonSerializerContext;
