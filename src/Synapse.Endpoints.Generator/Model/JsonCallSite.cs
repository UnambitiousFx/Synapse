namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     SYNE008: one type a low-level endpoint names explicitly in its own code — the type argument of
///     a <c>BodyAsync&lt;T&gt;</c> read, or of an <c>Accepts&lt;T&gt;</c>/<c>Produces&lt;T&gt;</c>
///     declaration — together with where it was written.
/// </summary>
/// <remarks>
///     A high-level endpoint's JSON-relevant types are its base class's type arguments, which the
///     generator reads directly. A low-level endpoint has none: the request it reads and the responses
///     it declares appear only as type arguments at call sites inside the class. Collecting them keeps
///     the same build-time Native AOT check working for both levels, which matters because an
///     unregistered type is otherwise a runtime failure rather than a build one.
/// </remarks>
internal readonly record struct JsonCallSite
{
    public JsonCallSite(string typeName,
        LocationInfo? location)
    {
        TypeName = typeName;
        Location = location;
    }

    /// <summary>The type's display name, in the same form as the registered names it is compared against.</summary>
    public string TypeName { get; }

    /// <summary>Where the type argument was written, so the diagnostic points at the call rather than the class.</summary>
    public LocationInfo? Location { get; }
}
