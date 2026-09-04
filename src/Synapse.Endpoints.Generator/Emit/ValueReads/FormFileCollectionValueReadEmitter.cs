using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>
///     Emits the read for a property bound to a group of uploaded files: either every file on the
///     request (<c>IFormFileCollection</c>, <see cref="Materialization.Native" />) or every file
///     filed under one field name (<c>IFormFile[]</c> and the other three collection shapes).
/// </summary>
/// <remarks>
///     Never reports "required": an absent file field, like any other absent collection, is simply
///     empty rather than a bind failure — the same rule <see cref="CollectionValueReadEmitter" />
///     follows for repeated query/header/form values.
/// </remarks>
internal static class FormFileCollectionValueReadEmitter
{
    internal static ValueRead Emit(ValueReadContext context)
    {
        var builder = context.Builder;
        var property = context.Property;
        var valueLocal = ValueReadEmitter.ValueLocal(property);

        if (property.Materialization == Materialization.Native)
        {
            // Every file on the request, regardless of field name — this is what makes
            // IFormFileCollection different from the other three collection shapes, and what ASP.NET
            // Core's own model binder does for the same type.
            builder.AppendLine("        var " + valueLocal + " = context.Request.Form.Files;");
            return new ValueRead(property, valueLocal, null, context.ConsumedByConstructor, context.SetInInitializer);
        }

        var listLocal = "list" + property.Name;
        var fieldLiteral = ValueReadEmitter.SourceKeyLiteral(property);

        builder.AppendLine(
            $"        var {listLocal} = {ValueReadEmitter.BindingNamespace}.BindingHelpers.GetFormFiles(context, {fieldLiteral});");

        // GetFormFiles returns IReadOnlyList<IFormFile>, which has no implicit conversion to
        // List<IFormFile> — so the List materialization (which also covers IReadOnlyList<T> and
        // IEnumerable<T>, all three sharing one Materialization value) is realised as a genuine
        // List<T> via ToList(), the same way the Array materialization goes through ToArray() rather
        // than assigning listLocal's own type directly. A real List<T> is assignable to all three of
        // its property shapes; passing listLocal through unconverted would compile only for the
        // IReadOnlyList<T>/IEnumerable<T> cases and fail CS0029 for a property declared List<T>.
        var assigned = property.Materialization == Materialization.Array
            ? $"global::System.Linq.Enumerable.ToArray({listLocal})"
            : $"global::System.Linq.Enumerable.ToList({listLocal})";

        builder.AppendLine($"        var {valueLocal} = {assigned};");

        return new ValueRead(property, valueLocal, null, context.ConsumedByConstructor, context.SetInInitializer);
    }
}
