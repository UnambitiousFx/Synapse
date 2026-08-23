namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>Where one property's value comes from.</summary>
internal enum BindingSource
{
    Route,
    Query,
    Header,
    Body
}

/// <summary>
///     Equatable description of one bindable property, produced by applying the five binding-source
///     resolution rules (spec section 4) to a message's properties.
/// </summary>
/// <remarks>
///     Declared with an explicit body rather than positional-record syntax for the same reason as
///     <see cref="EndpointTarget" />: a positional record's compiler-generated <c>init</c> accessor
///     needs <c>System.Runtime.CompilerServices.IsExternalInit</c>, which netstandard2.0 does not
///     supply and which this project does not polyfill. The <c>record</c> keyword is kept (rather
///     than a plain class) purely for the compiler-synthesized structural equality it gives for free,
///     which <see cref="EquatableArray{T}" /> depends on.
/// </remarks>
internal sealed record BindablePropertyModel
{
    public BindablePropertyModel(string name,
        string typeFullName,
        BindingSource source,
        string sourceKey,
        bool isNullable,
        bool isString,
        bool isEnum,
        bool isRecordWith)
    {
        Name = name;
        TypeFullName = typeFullName;
        Source = source;
        SourceKey = sourceKey;
        IsNullable = isNullable;
        IsString = isString;
        IsEnum = isEnum;
        IsRecordWith = isRecordWith;
    }

    /// <summary>The property's name on the bound type.</summary>
    public string Name { get; }

    /// <summary>
    ///     Fully-qualified name (<c>global::</c>-prefixed) of the property's type, or of the
    ///     underlying type when the property is a nullable value type.
    /// </summary>
    public string TypeFullName { get; }

    /// <summary>Where the value comes from.</summary>
    public BindingSource Source { get; }

    /// <summary>The route parameter name, query key, or header name to read.</summary>
    public string SourceKey { get; }

    /// <summary>
    ///     Whether a missing value is acceptable (the property is skipped) rather than a bind
    ///     failure.
    /// </summary>
    public bool IsNullable { get; }

    /// <summary>Whether the property's type is <see cref="string" />, so no parse step is emitted.</summary>
    public bool IsString { get; }

    /// <summary>Whether the property's type is an enum, so parsing goes through <c>Enum.TryParse</c>.</summary>
    public bool IsEnum { get; }

    /// <summary>
    ///     Whether the value is applied through a record <c>with</c> expression (the property is
    ///     <c>init</c>-only on a record) rather than a direct property assignment.
    /// </summary>
    public bool IsRecordWith { get; }
}
