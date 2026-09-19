using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Emit.ValueReads;

/// <summary>One property's read, as the construction and assignment steps need to see it.</summary>
internal readonly struct ValueRead
{
    internal ValueRead(BindablePropertyModel property,
        string valueLocal,
        string? presenceLocal,
        bool consumedByConstructor,
        bool setInInitializer)
    {
        Property = property;
        ValueLocal = valueLocal;
        PresenceLocal = presenceLocal;
        ConsumedByConstructor = consumedByConstructor;
        SetInInitializer = setInInitializer;
    }

    internal BindablePropertyModel Property { get; }

    internal string ValueLocal { get; }

    /// <summary>The local recording whether an optional value was present, or null when there is none.</summary>
    internal string? PresenceLocal { get; }

    internal bool ConsumedByConstructor { get; }

    /// <summary>Whether the value is applied in the object initializer of the <c>new</c> expression.</summary>
    internal bool SetInInitializer { get; }
}
