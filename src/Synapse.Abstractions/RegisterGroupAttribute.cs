namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a user-declared partial class as the target for the source-generated register group.
///     When present, the generator emits a partial of this class implementing
///     <see cref="IRegisterGroup" /> and <see cref="IEventDispatcherRegistration" /> — instead of
///     emitting its own <c>RegisterGroup</c> type in the assembly's root namespace — so the register
///     group's namespace, name and accessibility are chosen by the declaring class.
/// </summary>
/// <remarks>
///     The class must be a top-level (non-nested), non-generic <c>partial class</c>; otherwise the
///     generator reports a diagnostic. Declare at most one such class per assembly. When no class is
///     marked, the generator falls back to emitting <c>public sealed class RegisterGroup</c> in the
///     resolved root namespace.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RegisterGroupAttribute : Attribute;
