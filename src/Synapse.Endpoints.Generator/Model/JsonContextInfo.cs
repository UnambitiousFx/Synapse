namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     Every <c>[JsonSerializable(typeof(...))]</c> registration found anywhere in the compilation's
///     reference graph, plus whether at least one <c>JsonSerializerContext</c> exists at all. SYNE008
///     is reported only when <see cref="HasContext" /> is true — an app that has not opted into
///     source-generated JSON is not the target of that advice — and only for a type absent from
///     <see cref="RegisteredTypeNames" />.
/// </summary>
/// <remarks>
///     Declared with an explicit body rather than positional-record syntax, matching every other
///     pipeline-state type in this project (see <see cref="EndpointTarget" /> for why).
/// </remarks>
internal readonly record struct JsonContextInfo
{
    public JsonContextInfo(bool hasContext, EquatableArray<string> registeredTypeNames)
    {
        HasContext = hasContext;
        RegisteredTypeNames = registeredTypeNames;
    }

    /// <summary>Whether at least one type deriving from <c>JsonSerializerContext</c> was found.</summary>
    public bool HasContext { get; }

    /// <summary>
    ///     The display name (<see cref="Microsoft.CodeAnalysis.ISymbol.ToDisplayString()" />, default
    ///     format) of every type passed to <c>[JsonSerializable(typeof(...))]</c> on any discovered
    ///     context, across every discovered context combined.
    /// </summary>
    public EquatableArray<string> RegisteredTypeNames { get; }
}
