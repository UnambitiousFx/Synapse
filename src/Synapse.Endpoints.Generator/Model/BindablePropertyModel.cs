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
        bool isRecordWith,
        bool parsesWithFormatProvider,
        bool isReferenceType,
        bool isRequired)
    {
        Name = name;
        TypeFullName = typeFullName;
        Source = source;
        SourceKey = sourceKey;
        IsNullable = isNullable;
        IsString = isString;
        IsEnum = isEnum;
        IsRecordWith = isRecordWith;
        ParsesWithFormatProvider = parsesWithFormatProvider;
        IsReferenceType = isReferenceType;
        IsRequired = isRequired;
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

    /// <summary>
    ///     Whether the property's type exposes <c>TryParse(string, IFormatProvider, out T)</c>, so the
    ///     emitted parse can pin the invariant culture.
    /// </summary>
    /// <remarks>
    ///     Route and query values are a wire format and must not be read differently depending on the
    ///     server's locale, which is also what ASP.NET Core's own parameter binding does. Types that
    ///     only offer the two-argument <c>TryParse</c> (the minimum SYNE012 requires) still get that
    ///     one, because emitting an overload the type does not have would not compile.
    /// </remarks>
    public bool ParsesWithFormatProvider { get; }

    /// <summary>
    ///     Whether the property's type is a reference type, so a pre-declared local needs
    ///     <c>default!</c> rather than bare <c>default</c> to stay warning-free.
    /// </summary>
    public bool IsReferenceType { get; }

    /// <summary>
    ///     Whether the property is declared <c>required</c>, so it must be set in the object
    ///     initializer of the <c>new</c> expression rather than assigned afterwards.
    /// </summary>
    /// <remarks>
    ///     C# enforces <c>required</c> at the creation site (CS9035), and a later assignment or
    ///     <c>with</c> expression does not satisfy it. Constructing the message and then assigning
    ///     therefore did not compile for a message with a required member that no constructor
    ///     parameter covers. See docs/known-issues/061.
    /// </remarks>
    public bool IsRequired { get; }
}
