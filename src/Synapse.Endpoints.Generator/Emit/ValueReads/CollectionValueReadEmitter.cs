using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>
///     Emits the read for a property that takes every value of a repeated query, header or form key.
/// </summary>
/// <remarks>
///     An absent key yields an empty collection and reports nothing: HTTP cannot express zero values
///     under a key, so treating absence as a failure would demand something unsendable. A nullable
///     collection additionally records presence, so a caller that needs to tell "absent" from "empty"
///     still can. A bad element is reported at its index and the loop continues, so a request with
///     several bad elements answers with all of them.
/// </remarks>
internal static class CollectionValueReadEmitter
{
    internal static ValueRead Emit(ValueReadContext context)
    {
        var property = context.Property;
        var builder = context.Builder;

        var listLocal = "list" + property.Name;
        var rawLocal = ValueReadEmitter.RawLocal(property);
        var indexLocal = "index" + property.Name;
        var valueLocal = ValueReadEmitter.ValueLocal(property);
        var elementType = property.TypeFullName;
        var listType = $"global::System.Collections.Generic.List<{elementType}>";

        var fieldLiteral = ValueReadEmitter.SourceKeyLiteral(property);
        var sourceLabel = ValueReadEmitter.DescribeSource(property.Source);
        var messageHead = SymbolDisplay.FormatLiteral($"The {sourceLabel} at index ", quote: true);
        var messageTail = SymbolDisplay.FormatLiteral(
            $" is not a valid {ValueReadEmitter.DisplayTypeName(property)}.", quote: true);

        var presenceLocal = property.IsNullable && !context.AppliedUnconditionally
            ? "has" + property.Name
            : null;

        if (presenceLocal is not null)
        {
            builder.AppendLine($"        var {presenceLocal} = false;");
        }

        builder.AppendLine($"        var {listLocal} = new {listType}();");
        builder.AppendLine(
            $"        if ({ValueReadEmitter.BindingNamespace}.BindingHelpers.{GetTryGetValuesMethod(property.Source)}(context, {fieldLiteral}, out var {rawLocal}))");
        builder.AppendLine("        {");

        if (presenceLocal is not null)
        {
            builder.AppendLine($"            {presenceLocal} = true;");
        }

        builder.AppendLine($"            for (var {indexLocal} = 0; {indexLocal} < {rawLocal}.Count; {indexLocal}++)");
        builder.AppendLine("            {");

        if (property.IsString)
        {
            builder.AppendLine($"                {listLocal}.Add({rawLocal}[{indexLocal}] ?? string.Empty);");
        }
        else
        {
            var parsedLocal = "parsed" + property.Name;
            builder.AppendLine(
                $"                if ({ValueReadEmitter.TryParseExpression(property, $"{rawLocal}[{indexLocal}]", "var " + parsedLocal)})");
            builder.AppendLine("                {");
            builder.AppendLine($"                    {listLocal}.Add({parsedLocal});");
            builder.AppendLine("                }");
            builder.AppendLine("                else");
            builder.AppendLine("                {");

            // Concatenated rather than interpolated so the index is formatted with the invariant
            // culture, matching every other wire-value decision the generated code makes.
            builder.AppendLine(
                $"                    validation.AddError({fieldLiteral}, {messageHead} + " +
                $"{indexLocal}.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + {messageTail});");
            builder.AppendLine("                }");
        }

        builder.AppendLine("            }");
        builder.AppendLine("        }");

        var assigned = property.Materialization == Materialization.Array
            ? $"{listLocal}.ToArray()"
            : listLocal;

        builder.AppendLine($"        var {valueLocal} = {assigned};");

        return new ValueRead(property, valueLocal, presenceLocal,
            context.ConsumedByConstructor, context.SetInInitializer);
    }

    private static string GetTryGetValuesMethod(BindingSource source)
    {
        // Form joins these two in Task 11, once BindingSource.Form exists. Route and Body never
        // will: a route segment cannot repeat, and a JSON array is already bound by the serializer.
        return source switch
        {
            BindingSource.Query => "TryGetQueryValues",
            BindingSource.Header => "TryGetHeaderValues",
            _ => throw new InvalidOperationException(
                $"'{source}' cannot repeat, so it has no collection reader.")
        };
    }
}
