namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     One parameter of the constructor a bodyless binder uses to construct a bound type that has no
///     parameterless constructor (see <see cref="EndpointTarget.PrimaryConstructorParameters" />).
/// </summary>
/// <remarks>
///     Carries <see cref="IsReferenceType" /> alongside the name because a parameter with no matching
///     bindable property is emitted as a literal default value; for a reference type that must be
///     <c>default!</c> rather than bare <c>default</c>, or nullable-reference analysis on the
///     generated code raises a warning (a real defect fixed in Task 17 — <c>TreatWarningsAsErrors</c>
///     consumers would otherwise fail a build our own test suite never reproduces, since
///     <c>AssertGeneratedCompiles</c> only fails on <see cref="Microsoft.CodeAnalysis.DiagnosticSeverity.Error" />).
///     Declared with an explicit body rather than positional-record syntax, matching every other
///     pipeline-state type in this project (see <see cref="EndpointTarget" /> for why).
/// </remarks>
internal sealed record ConstructorParameterModel
{
    public ConstructorParameterModel(string name,
        bool isReferenceType,
        string? matchedPropertyName,
        string? defaultValueExpression)
    {
        Name = name;
        IsReferenceType = isReferenceType;
        MatchedPropertyName = matchedPropertyName;
        DefaultValueExpression = defaultValueExpression;
    }

    /// <summary>The constructor parameter's name.</summary>
    public string Name { get; }

    /// <summary>
    ///     The name of the bindable property whose value this parameter takes, or
    ///     <see langword="null" /> when no property matches it.
    /// </summary>
    /// <remarks>
    ///     Resolved during analysis, where the parameter's and the property's type symbols are both
    ///     available, so a parameter is matched only when the property's value can actually be passed
    ///     to it. Matching on the name alone — which is all the emitter can do from strings — paired an
    ///     <c>int? Page</c> property with an <c>int page</c> parameter and emitted an argument that
    ///     does not convert (CS1503), or a <c>string?</c> property with a <c>string</c> parameter for a
    ///     CS8604 warning that fails a <c>TreatWarningsAsErrors</c> build. See
    ///     docs/known-issues/059.
    /// </remarks>
    public string? MatchedPropertyName { get; }

    /// <summary>
    ///     The parameter's default value as a C# expression, or <see langword="null" /> when the
    ///     parameter has no default.
    /// </summary>
    /// <remarks>
    ///     Used both for a parameter no property matches and as the initial value of a matched
    ///     property's local, so an absent optional value falls back to the default the constructor
    ///     declares instead of overwriting it with <c>default</c>. See docs/known-issues/060.
    /// </remarks>
    public string? DefaultValueExpression { get; }

    /// <summary>
    ///     Whether the parameter's type is a reference type, so an unmatched parameter must be
    ///     emitted as <c>default!</c> rather than bare <c>default</c> to avoid a nullable-reference
    ///     warning in the generated code.
    /// </summary>
    public bool IsReferenceType { get; }
}
