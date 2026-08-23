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
    public ConstructorParameterModel(string name, bool isReferenceType)
    {
        Name = name;
        IsReferenceType = isReferenceType;
    }

    /// <summary>The constructor parameter's name.</summary>
    public string Name { get; }

    /// <summary>
    ///     Whether the parameter's type is a reference type, so an unmatched parameter must be
    ///     emitted as <c>default!</c> rather than bare <c>default</c> to avoid a nullable-reference
    ///     warning in the generated code.
    /// </summary>
    public bool IsReferenceType { get; }
}
