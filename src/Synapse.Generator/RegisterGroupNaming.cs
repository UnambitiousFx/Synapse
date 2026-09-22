namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Resolves the namespace and class name a compilation's generated <c>RegisterGroup</c> is emitted under:
///     the class marked <c>[RegisterGroup]</c> when one was declared and validated, otherwise the default
///     <c>RegisterGroup</c> in the assembly's root namespace. Shared by <see cref="SynapseGenerator" /> (which
///     emits under this name) and the <c>SYN105</c> analyzer (which needs to know what name to look for in an
///     <c>AddRegisterGroup</c> call), so the two can never disagree.
/// </summary>
internal static class RegisterGroupNaming
{
    /// <summary>
    ///     Resolves the emitted namespace and class name.
    /// </summary>
    /// <param name="rootNamespace">
    ///     The assembly's root namespace, used only when <paramref name="customTarget" /> is <see langword="null" />.
    /// </param>
    /// <param name="customTarget">
    ///     The namespace and class name of a valid <c>[RegisterGroup]</c>-attributed class, or <see langword="null" />
    ///     when none was declared (or the one declared was invalid — callers resolve validity before calling this).
    /// </param>
    public static (string Namespace, string ClassName) Resolve(string rootNamespace,
        (string Namespace, string ClassName)? customTarget)
    {
        return customTarget is { } target
            ? (target.Namespace, target.ClassName)
            : (rootNamespace, "RegisterGroup");
    }
}
