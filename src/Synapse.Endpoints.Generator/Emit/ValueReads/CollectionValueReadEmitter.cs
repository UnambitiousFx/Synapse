using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>
///     Emits the read for a property that takes every value of a repeated query, header or form key.
/// </summary>
/// <remarks>
///     An absent key yields an empty collection and reports nothing: HTTP cannot express zero values
///     under a key, so treating absence as a failure would demand something unsendable. A nullable
///     collection binds null instead, so a caller that needs to tell "absent" from "empty" still can —
///     recorded as a presence flag where the binder assigns the property afterwards, and as a
///     pre-declared null local where construction applies the value unconditionally and there is no
///     assignment to guard. A bad element is reported at its index and the loop continues, so a
///     request with several bad elements answers with all of them.
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

        // A nullable collection whose value is applied unconditionally — a primary-constructor
        // argument, or the object initializer a `required` member needs — has no presence flag to
        // guard the assignment with, so the null has to live in the local itself: it is pre-declared
        // null and only overwritten once the key is seen. Without this the emitter assigned the
        // materialised (empty) list whatever happened, and "absent binds null" held for the plain
        // settable property alone, which is one of the three shapes a message can be built in.
        var nullWhenAbsent = property.IsNullable && context.AppliedUnconditionally;
        var collectionType = property.Materialization == Materialization.Array
            ? $"{elementType}[]"
            : listType;

        if (nullWhenAbsent)
        {
            builder.AppendLine($"        {collectionType}? {valueLocal} = default;");
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

        var assigned = property.Materialization == Materialization.Array
            ? $"{listLocal}.ToArray()"
            : listLocal;

        if (nullWhenAbsent)
        {
            builder.AppendLine($"            {valueLocal} = {assigned};");
        }

        builder.AppendLine("        }");

        if (!nullWhenAbsent)
        {
            builder.AppendLine($"        var {valueLocal} = {assigned};");
        }

        return new ValueRead(property, valueLocal, presenceLocal,
            context.ConsumedByConstructor, context.SetInInitializer);
    }

    private static string GetTryGetValuesMethod(BindingSource source)
    {
        // Route and Body never join this set: a route segment cannot repeat, and a JSON array is
        // already bound by the serializer.
        return source switch
        {
            BindingSource.Query => "TryGetQueryValues",
            BindingSource.Header => "TryGetHeaderValues",
            BindingSource.Form => "TryGetFormValues",
            _ => throw new InvalidOperationException(
                $"'{source}' cannot repeat, so it has no collection reader.")
        };
    }
}
