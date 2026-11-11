namespace UnambitiousFx.Synapse.Abstractions.Exceptions;

/// <summary>
///     Thrown when the behaviors of a pipeline cannot be ordered: their <c>Before</c>/<c>After</c> constraints form
///     a cycle, or a constraint contradicts the stages the behaviors run in. <c>ValidateSynapse</c> reports the same
///     problems at startup as SYN005 and SYN006.
/// </summary>
public sealed class PipelineOrderingException : SynapseException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineOrderingException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="isCycle"><c>true</c> for a cycle; <c>false</c> for a constraint that contradicts the stages.</param>
    /// <param name="behaviorTypes">The behaviors involved.</param>
    public PipelineOrderingException(string message, bool isCycle, IReadOnlyList<Type> behaviorTypes)
        : base(message)
    {
        IsCycle = isCycle;
        BehaviorTypes = behaviorTypes;
    }

    /// <summary><c>true</c> when the constraints form a cycle; <c>false</c> when one contradicts the stages.</summary>
    public bool IsCycle { get; }

    /// <summary>The behaviors involved in the conflict.</summary>
    public IReadOnlyList<Type> BehaviorTypes { get; }
}
