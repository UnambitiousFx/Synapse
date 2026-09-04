using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>
///     Dispatches one property's read to the emitter for its <see cref="BindingValueShape" />, and
///     owns the naming and literal helpers all of them share.
/// </summary>
/// <remarks>
///     Split out of <c>BinderEmitter</c> because the read is the only part that varies per property:
///     construction, assignment and the presence-flag contract are the same whatever the shape. The
///     alternative — one method branching over source, cardinality, nullability, parse kind and
///     construction — is the shape this replaces.
/// </remarks>
internal static class ValueReadEmitter
{
    internal const string BindingNamespace = "global::UnambitiousFx.Synapse.Endpoints.Binding";

    internal static ValueRead Emit(ValueReadContext context)
    {
        return ScalarValueReadEmitter.Emit(context);
    }

    internal static string ValueLocal(BindablePropertyModel property)
    {
        return "value" + property.Name;
    }

    internal static string RawLocal(BindablePropertyModel property)
    {
        return "raw" + property.Name;
    }

    internal static string SourceKeyLiteral(BindablePropertyModel property)
    {
        return SymbolDisplay.FormatLiteral(property.SourceKey, quote: true);
    }

    internal static string DisplayTypeName(BindablePropertyModel property)
    {
        return property.TypeFullName.StartsWith("global::", StringComparison.Ordinal)
            ? property.TypeFullName.Substring("global::".Length)
            : property.TypeFullName;
    }

    internal static string DescribeSource(BindingSource source)
    {
        return source switch
        {
            BindingSource.Route => "route value",
            BindingSource.Query => "query value",
            BindingSource.Header => "header",
            _ => throw new InvalidOperationException($"Unexpected binding source '{source}'.")
        };
    }

    internal static string GetTryGetMethod(BindingSource source)
    {
        return source switch
        {
            BindingSource.Route => "TryGetRoute",
            BindingSource.Query => "TryGetQuery",
            BindingSource.Header => "TryGetHeader",
            _ => throw new InvalidOperationException($"Unexpected binding source '{source}'.")
        };
    }

    internal static string TryParseExpression(BindablePropertyModel property,
        string rawExpression,
        string outTarget)
    {
        if (property.IsEnum)
        {
            return $"global::System.Enum.TryParse<{property.TypeFullName}>({rawExpression}, out {outTarget})";
        }

        return property.ParsesWithFormatProvider
            ? $"{property.TypeFullName}.TryParse({rawExpression}, global::System.Globalization.CultureInfo.InvariantCulture, out {outTarget})"
            : $"{property.TypeFullName}.TryParse({rawExpression}, out {outTarget})";
    }
}
