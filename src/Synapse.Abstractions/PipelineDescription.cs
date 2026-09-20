using System.Text;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The handler(s) and behaviors a message type resolves to, as the dispatcher runs them.
///     Equality is structural: two descriptions are equal when they list the same handlers and the same behaviors
///     in the same order, so a test can compare whole descriptions.
/// </summary>
/// <param name="Handlers">
///     The handler types: exactly one for a request, one per subscriber for an event. For an event they are in the
///     order the dispatcher fans out to, which is registration order.
/// </param>
/// <param name="Behaviors">
///     The behaviors in execution order, outermost first. Behaviors that share an <c>Order</c> keep their
///     registration order.
/// </param>
public sealed record PipelineDescription(
    IReadOnlyList<Type> Handlers,
    IReadOnlyList<BehaviorDescription> Behaviors)
{
    /// <summary>
    ///     Compares the handlers and behaviors element by element, in order.
    /// </summary>
    /// <param name="other">The description to compare with.</param>
    /// <returns><c>true</c> when both lists match; otherwise <c>false</c>.</returns>
    public bool Equals(PipelineDescription? other)
    {
        return other is not null &&
               Handlers.SequenceEqual(other.Handlers) &&
               Behaviors.SequenceEqual(other.Behaviors);
    }

    /// <summary>
    ///     Combines the handlers and behaviors in order, consistent with <see cref="Equals(PipelineDescription)" />.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var handler in Handlers)
        {
            hash.Add(handler);
        }

        foreach (var behavior in Behaviors)
        {
            hash.Add(behavior);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    ///     Lists the handlers, then the behaviors as <c>Order Name</c>, outermost first.
    /// </summary>
    /// <returns>A readable multi-line description.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append("Handlers: ").AppendJoin(", ", Handlers.Select(handler => handler.Name));
        text.Append("; Behaviors (outermost first): ")
            .AppendJoin(", ", Behaviors.Select(behavior => $"{behavior.Order} {behavior.Type.Name}"));
        return text.ToString();
    }
}
