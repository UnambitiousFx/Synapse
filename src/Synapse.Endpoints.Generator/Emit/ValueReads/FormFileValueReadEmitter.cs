using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>
///     Emits the read for a property bound to one uploaded file (<c>IFormFile</c>).
/// </summary>
/// <remarks>
///     There is no parse step for a file — a stream has no textual form to <c>TryParse</c> — so
///     SYNE012 never applies to it, but the value still has to be assignable, so SYNE011 does (see
///     <c>ResolveBindableProperty</c>'s file-shape branch, which checks assignability before ever
///     reaching this emitter). A non-nullable file that is absent is a bind failure, exactly like any
///     other required value; a nullable one simply records presence, mirroring
///     <see cref="ScalarValueReadEmitter" />'s optional-value handling so an absent optional file
///     leaves a property initializer's default alone.
/// </remarks>
internal static class FormFileValueReadEmitter
{
    internal static ValueRead Emit(ValueReadContext context)
    {
        var builder = context.Builder;
        var property = context.Property;
        var consumedByConstructor = context.ConsumedByConstructor;
        var setInInitializer = context.SetInInitializer;

        var fieldLiteral = ValueReadEmitter.SourceKeyLiteral(property);
        var rawLocal = ValueReadEmitter.RawLocal(property);
        var valueLocal = ValueReadEmitter.ValueLocal(property);

        if (!property.IsNullable)
        {
            var requiredMessage = SymbolDisplay.FormatLiteral("The form file is required.", quote: true);

            // Pre-declared as default! (not bare default): IFormFile is a reference type, and a
            // reference-typed local left at bare `default` warns CS8600 on the later assignment under
            // TreatWarningsAsErrors.
            builder.AppendLine($"        {property.TypeFullName} {valueLocal} = default!;");
            builder.AppendLine(
                $"        if (!{ValueReadEmitter.BindingNamespace}.BindingHelpers.TryGetFormFile(context, {fieldLiteral}, out var {rawLocal}))");
            builder.AppendLine("        {");
            builder.AppendLine($"            validation.AddError({fieldLiteral}, {requiredMessage});");
            builder.AppendLine("        }");
            builder.AppendLine("        else");
            builder.AppendLine("        {");
            builder.AppendLine($"            {valueLocal} = {rawLocal}!;");
            builder.AppendLine("        }");

            return new ValueRead(property, valueLocal, null, consumedByConstructor, setInInitializer);
        }

        // Nullable: an absent file is not an error. No presence flag where construction applies the
        // value unconditionally (a constructor argument, or the object initializer a `required`
        // property needs), since nothing would ever read it and an unread local warns in the
        // generated code — the same rule ScalarValueReadEmitter follows.
        var presenceLocal = consumedByConstructor || setInInitializer ? null : "has" + property.Name;
        if (presenceLocal is not null)
        {
            builder.AppendLine($"        var {presenceLocal} = false;");
        }

        builder.AppendLine($"        {property.TypeFullName}? {valueLocal} = default;");
        builder.AppendLine(
            $"        if ({ValueReadEmitter.BindingNamespace}.BindingHelpers.TryGetFormFile(context, {fieldLiteral}, out var {rawLocal}))");
        builder.AppendLine("        {");
        builder.AppendLine($"            {valueLocal} = {rawLocal};");
        if (presenceLocal is not null)
        {
            builder.AppendLine($"            {presenceLocal} = true;");
        }

        builder.AppendLine("        }");

        return new ValueRead(property, valueLocal, presenceLocal, consumedByConstructor, setInInitializer);
    }
}
