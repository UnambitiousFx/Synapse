namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Restores same-assembly-only pipeline behavior wiring for the decorated assembly.
/// </summary>
/// <remarks>
///     By default the Synapse source generator applies an assembly's open-generic
///     <c>[PipelineBehavior]</c> classes to every request, event, and stream handler it can see —
///     including handlers defined in referenced assemblies — so a behavior registered in the
///     composition root blankets the whole reference graph (it emits one closed, AOT-safe
///     registration per matching handler). Apply this attribute to opt the assembly out of that
///     downward propagation, limiting behavior cross-product to handlers declared in this assembly.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableSynapseCrossAssemblyBehaviorsAttribute : Attribute;
