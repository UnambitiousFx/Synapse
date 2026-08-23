namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     Per-distinct-bound-type inputs to <c>BinderEmitter</c> and the registration emitter, gathered
///     from whichever endpoint sorts first (by <c>EndpointTarget.EndpointFullName</c>) among the
///     endpoints sharing that bound type. Unlike <see cref="EndpointTarget" /> and
///     <see cref="BindablePropertyModel" />, this is assembled after the incremental pipeline's
///     caching point (in <c>EndpointsGenerator.Emit</c>), so it does not need value equality.
/// </summary>
internal sealed class BoundTypeInfo
{
    public BoundTypeInfo(string typeFullName,
        EquatableArray<BindablePropertyModel> properties,
        bool isBodylessVerb,
        bool hasParameterlessConstructor,
        EquatableArray<ConstructorParameterModel> primaryConstructorParameters)
    {
        TypeFullName = typeFullName;
        Properties = properties;
        IsBodylessVerb = isBodylessVerb;
        HasParameterlessConstructor = hasParameterlessConstructor;
        PrimaryConstructorParameters = primaryConstructorParameters;
    }

    /// <summary>Fully-qualified name of the bound type.</summary>
    public string TypeFullName { get; }

    /// <summary>The type's resolved bindable properties.</summary>
    public EquatableArray<BindablePropertyModel> Properties { get; }

    /// <summary>Whether the winning endpoint's HTTP verb is one that never carries a body.</summary>
    public bool IsBodylessVerb { get; }

    /// <summary>Whether the type has an accessible parameterless constructor.</summary>
    public bool HasParameterlessConstructor { get; }

    /// <summary>
    ///     The parameters of the constructor to use when <see cref="HasParameterlessConstructor" />
    ///     is false. Empty when it is true.
    /// </summary>
    public EquatableArray<ConstructorParameterModel> PrimaryConstructorParameters { get; }
}
