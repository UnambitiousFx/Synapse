namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Describes a class decorated with <c>[RegisterGroup]</c> discovered by the source generator. When a
///     single valid target exists, the generated register group is emitted as a partial of this class
///     (using its <see cref="Namespace" /> and <see cref="ClassName" />) instead of the default
///     <c>public sealed class RegisterGroup</c> in the assembly root namespace.
/// </summary>
public readonly record struct RegisterGroupTarget
{
    public RegisterGroupTarget(string @namespace,
        string className,
        bool isPartial,
        bool isNested,
        bool isGeneric,
        LocationInfo? location)
    {
        Namespace = @namespace;
        ClassName = className;
        IsPartial = isPartial;
        IsNested = isNested;
        IsGeneric = isGeneric;
        Location = location;
    }

    /// <summary>Namespace of the declaring class (empty for the global namespace).</summary>
    public string Namespace { get; }

    /// <summary>Simple class name (without namespace).</summary>
    public string ClassName { get; }

    /// <summary>True when the class is declared <c>partial</c> — required so the generator can extend it.</summary>
    public bool IsPartial { get; }

    /// <summary>True when the class is nested inside another type (unsupported).</summary>
    public bool IsNested { get; }

    /// <summary>True when the class declares type parameters (unsupported).</summary>
    public bool IsGeneric { get; }

    /// <summary>Location of the class declaration, used to anchor diagnostics.</summary>
    public LocationInfo? Location { get; }
}
