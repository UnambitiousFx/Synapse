namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a class as a request validator that should be automatically registered by the source generator.
///     The generator reads the validated request type from the implemented
///     <see cref="IRequestValidator{TRequest}" /> interface, derives the response type (if any) from the
///     request's <see cref="IRequest{TResponse}" /> implementation, and emits a closed (Native-AOT safe)
///     registration that wires both the validator and the <c>RequestValidationBehavior</c> that runs it.
/// </summary>
/// <remarks>
///     Applying this attribute replaces the need to call <c>cfg.AddValidator&lt;...&gt;()</c> at runtime.
///     The class must implement <see cref="IRequestValidator{TRequest}" />; otherwise the generator reports a
///     diagnostic and emits nothing for it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ValidatorAttribute : Attribute
{
}
