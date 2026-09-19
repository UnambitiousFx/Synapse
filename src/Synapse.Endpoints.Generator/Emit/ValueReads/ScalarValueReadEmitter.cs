using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>
///     Emits the read, parse and error collection for a scalar-shaped property (a route, query or
///     header value that is a string, an enum, or a type with <c>TryParse</c>), into a local that the
///     construction or assignment step later reads.
/// </summary>
internal static class ScalarValueReadEmitter
{
    internal static ValueRead Emit(ValueReadContext context)
    {
        var builder = context.Builder;
        var property = context.Property;
        var consumedByConstructor = context.ConsumedByConstructor;
        var setInInitializer = context.SetInInitializer;
        var constructorDefault = context.ConstructorDefault;

        var tryGetMethod = ValueReadEmitter.GetTryGetMethod(property.Source);
        var sourceKeyLiteral = SymbolDisplay.FormatLiteral(property.SourceKey, quote: true);
        var fieldLiteral = SymbolDisplay.FormatLiteral(property.SourceKey, quote: true);
        var rawLocal = ValueReadEmitter.RawLocal(property);
        var valueLocal = ValueReadEmitter.ValueLocal(property);
        var sourceLabel = ValueReadEmitter.DescribeSource(property.Source);

        var requiredMessage = SymbolDisplay.FormatLiteral($"The {sourceLabel} is required.", quote: true);
        var notValidMessage = SymbolDisplay.FormatLiteral(
            $"The {sourceLabel} is not a valid {ValueReadEmitter.DisplayTypeName(property)}.", quote: true);

        if (!property.IsNullable)
        {
            // A constructor parameter's default makes the value optional: the type said what it wants
            // when nothing is sent, so an absent value is not an error and the default stands. Without
            // this, `record ListUsers(int Page = 1)` answered 400 for a request that omitted `page`,
            // and a nullable parameter's default was overwritten with null. See
            // docs/known-issues/060.
            if (constructorDefault is not null)
            {
                builder.AppendLine($"        {property.TypeFullName} {valueLocal} = {constructorDefault};");
                builder.AppendLine(
                    $"        if ({ValueReadEmitter.BindingNamespace}.BindingHelpers.{tryGetMethod}(context, {sourceKeyLiteral}, out var {rawLocal}))");
                builder.AppendLine("        {");

                if (property.IsString)
                {
                    builder.AppendLine($"            {valueLocal} = {rawLocal}!;");
                }
                else
                {
                    builder.AppendLine(
                        $"            if (!{ValueReadEmitter.TryParseExpression(property, rawLocal, valueLocal)})");
                    builder.AppendLine("            {");
                    builder.AppendLine($"                validation.AddError({fieldLiteral}, {notValidMessage});");
                    builder.AppendLine("            }");
                }

                builder.AppendLine("        }");

                return new ValueRead(property, valueLocal, null, consumedByConstructor, setInInitializer);
            }

            var initializer = property.IsReferenceType ? "default!" : "default";
            builder.AppendLine($"        {property.TypeFullName} {valueLocal} = {initializer};");
            builder.AppendLine(
                $"        if (!{ValueReadEmitter.BindingNamespace}.BindingHelpers.{tryGetMethod}(context, {sourceKeyLiteral}, out var {rawLocal}))");
            builder.AppendLine("        {");
            builder.AppendLine($"            validation.AddError({fieldLiteral}, {requiredMessage});");
            builder.AppendLine("        }");

            if (property.IsString)
            {
                builder.AppendLine("        else");
                builder.AppendLine("        {");
                builder.AppendLine($"            {valueLocal} = {rawLocal}!;");
                builder.AppendLine("        }");
            }
            else
            {
                builder.AppendLine(
                    $"        else if (!{ValueReadEmitter.TryParseExpression(property, rawLocal, valueLocal)})");
                builder.AppendLine("        {");
                builder.AppendLine($"            validation.AddError({fieldLiteral}, {notValidMessage});");
                builder.AppendLine("        }");
            }

            return new ValueRead(property, valueLocal, null, consumedByConstructor, setInInitializer);
        }

        // A nullable property is optional: an absent value is not an error. The presence flag keeps an
        // absent value from overwriting a property initializer with null, which a bare assignment
        // would do.
        // No presence flag where construction applies the value unconditionally — as a constructor
        // argument, or in the object initializer a `required` property needs — since nothing would
        // ever read it and an unread local is a warning in the generated code.
        var presenceLocal = consumedByConstructor || setInInitializer ? null : "has" + property.Name;
        if (presenceLocal is not null)
        {
            builder.AppendLine($"        var {presenceLocal} = false;");
        }

        builder.AppendLine(
            $"        {property.TypeFullName}? {valueLocal} = {constructorDefault ?? "default"};");
        builder.AppendLine(
            $"        if ({ValueReadEmitter.BindingNamespace}.BindingHelpers.{tryGetMethod}(context, {sourceKeyLiteral}, out var {rawLocal}))");
        builder.AppendLine("        {");

        if (property.IsString)
        {
            builder.AppendLine($"            {valueLocal} = {rawLocal};");
            if (presenceLocal is not null)
            {
                builder.AppendLine($"            {presenceLocal} = true;");
            }
        }
        else
        {
            var parsedLocal = "parsed" + property.Name;
            builder.AppendLine(
                $"            if (!{ValueReadEmitter.TryParseExpression(property, rawLocal, "var " + parsedLocal)})");
            builder.AppendLine("            {");
            builder.AppendLine($"                validation.AddError({fieldLiteral}, {notValidMessage});");
            builder.AppendLine("            }");
            builder.AppendLine("            else");
            builder.AppendLine("            {");
            builder.AppendLine($"                {valueLocal} = {parsedLocal};");
            if (presenceLocal is not null)
            {
                builder.AppendLine($"                {presenceLocal} = true;");
            }

            builder.AppendLine("            }");
        }

        builder.AppendLine("        }");

        return new ValueRead(property, valueLocal, presenceLocal, consumedByConstructor, setInInitializer);
    }
}
