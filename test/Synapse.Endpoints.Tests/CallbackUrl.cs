using System.Diagnostics.CodeAnalysis;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

/// <summary>
///     A reference type that implements <see cref="IParsable{TSelf}" /> and can fail to parse — the
///     shape the optional readers could not express while they were constrained to
///     <see langword="struct" />. A <see langword="string" /> alone would not prove much: it never
///     fails to parse, so it cannot show that a present-but-invalid optional value is still reported.
/// </summary>
internal sealed class CallbackUrl : IParsable<CallbackUrl>
{
    private CallbackUrl(Uri value)
    {
        Value = value;
    }

    public Uri Value { get; }

    public static CallbackUrl Parse(string s,
        IFormatProvider? provider)
    {
        return TryParse(s, provider, out var result)
            ? result
            : throw new FormatException($"'{s}' is not an absolute http(s) URL.");
    }

    public static bool TryParse(string? s,
        IFormatProvider? provider,
        [MaybeNullWhen(false)] out CallbackUrl result)
    {
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            result = new CallbackUrl(uri);
            return true;
        }

        result = null;
        return false;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
