using System.Text;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>
///     Everything a shape emitter needs to write one property's read: the buffer to write into, the
///     resolved property, and how construction will consume the value it produces.
/// </summary>
/// <remarks>
///     A struct passed by value: it is created once per property inside a loop that already runs per
///     bound type, and carries only references and flags.
/// </remarks>
internal readonly struct ValueReadContext
{
    internal ValueReadContext(StringBuilder builder,
        BindablePropertyModel property,
        bool consumedByConstructor,
        bool setInInitializer,
        string? constructorDefault)
    {
        Builder = builder;
        Property = property;
        ConsumedByConstructor = consumedByConstructor;
        SetInInitializer = setInInitializer;
        ConstructorDefault = constructorDefault;
    }

    internal StringBuilder Builder { get; }

    internal BindablePropertyModel Property { get; }

    /// <summary>Whether the value is passed to the type's primary constructor.</summary>
    internal bool ConsumedByConstructor { get; }

    /// <summary>Whether the value is set in the object initializer a <c>required</c> member needs.</summary>
    internal bool SetInInitializer { get; }

    /// <summary>
    ///     The default expression of the constructor parameter consuming this value, or
    ///     <see langword="null" />. A parameter's own default makes the value optional: the type
    ///     already said what it wants when nothing is sent.
    /// </summary>
    internal string? ConstructorDefault { get; }

    /// <summary>
    ///     Whether construction applies the value unconditionally, so a presence flag would never be
    ///     read — and an unread local is a warning in the generated code.
    /// </summary>
    internal bool AppliedUnconditionally => ConsumedByConstructor || SetInInitializer;
}
