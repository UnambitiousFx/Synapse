using System.Collections.Concurrent;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Resolves the execution order of a pipeline's behaviors from their stages, their <c>Before</c>/<c>After</c>
///     constraints, their <see cref="IOrderedPipelineBehavior.Order" /> and their registration order.
/// </summary>
/// <remarks>
///     <para>
///         The algorithm: each behavior belongs to its declared stage, or to the stage whose
///         <see cref="PipelineStages" /> band contains its <c>Order</c>. Stages run in enum order. A constraint between
///         two behaviors of different stages must agree with the stage order (otherwise it is impossible); within a
///         stage it becomes an edge. A stable topological sort (Kahn) then always picks, among the behaviors whose
///         predecessors are placed, the one with the lowest (stage, <c>Order</c>, registration index). With no
///         declarations this is exactly the stable sort by <c>Order</c> the pipeline always used, because the bands are
///         contiguous and increasing.
///     </para>
///     <para>
///         One instance per container. The resolved permutation is cached per pipeline and reused as long as the same
///         behavior types with the same <c>Order</c>s come back, so it is computed once rather than every time a
///         scope builds the pipeline. Pipelines without declarations go through the same cache: they get the
///         <c>Order</c> sort they always had, without re-sorting per scope.
///     </para>
/// </remarks>
internal sealed class PipelineOrdering
{
    private readonly ConcurrentDictionary<Type, Plan> _plans = new();
    private readonly Dictionary<Type, MergedRule> _rules = new();

    public PipelineOrdering(IEnumerable<BehaviorOrderingRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!_rules.TryGetValue(rule.BehaviorType, out var merged))
            {
                _rules[rule.BehaviorType] = merged = new MergedRule();
            }

            if (rule.Stage is { } stage)
            {
                if (merged.Stage is { } existing && existing != stage)
                {
                    throw new PipelineOrderingException(
                        $"'{DisplayName(rule.BehaviorType)}' is declared in two stages, {existing} and {stage}. " +
                        "Declare a single stage.", false, [rule.BehaviorType]);
                }

                merged.Stage = stage;
            }

            AddDistinct(merged.Before, rule.Before);
            AddDistinct(merged.After, rule.After);
        }
    }

    /// <summary>An ordering with no declarations, for proxies built outside a container.</summary>
    public static PipelineOrdering Empty { get; } = new([]);

    /// <summary>
    ///     Whether <paramref name="type" /> is <paramref name="target" />, or a closed form of it when
    ///     <paramref name="target" /> is an open-generic definition.
    /// </summary>
    public static bool Matches(Type type, Type target)
    {
        return type == target ||
               (target.IsGenericTypeDefinition && type.IsGenericType && type.GetGenericTypeDefinition() == target);
    }

    /// <summary>
    ///     Orders <paramref name="items" /> (given in registration order) by pipeline position.
    /// </summary>
    /// <param name="pipeline">A key identifying the pipeline, e.g. the closed proxy type.</param>
    /// <param name="items">The items, in registration order.</param>
    /// <param name="behaviorOf">Gets the behavior instance of an item.</param>
    /// <exception cref="PipelineOrderingException">The constraints form a cycle or contradict the stages.</exception>
    public T[] Sort<T>(Type pipeline, T[] items, Func<T, object> behaviorOf)
    {
        if (items.Length < 2)
        {
            return items;
        }

        if (!_plans.TryGetValue(pipeline, out var plan) || !plan.Matches(items, behaviorOf))
        {
            plan = CreatePlan(items, behaviorOf);
            _plans[pipeline] = plan;
        }

        var sorted = new T[items.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            sorted[i] = items[plan.Permutation[i]];
        }

        return sorted;
    }

    /// <summary>The stage a behavior runs in: the declared one, or the one implied by its <c>Order</c>.</summary>
    public PipelineStage StageOf(object behavior)
    {
        return Find(behavior.GetType())?.Stage ?? PipelineStages.Of(PipelineBehaviorOrdering.OrderOf(behavior));
    }

    /// <summary>
    ///     Whether a chain of <c>Before</c>/<c>After</c> constraints between the behaviors of
    ///     <paramref name="pipeline" /> orders <paramref name="first" /> and <paramref name="second" /> (in either
    ///     direction), so their relative order is not decided by registration order.
    /// </summary>
    /// <param name="pipeline">The behavior types of one pipeline.</param>
    /// <param name="first">One behavior of the pipeline.</param>
    /// <param name="second">Another behavior of the pipeline.</param>
    public bool AreOrdered(IReadOnlyList<Type> pipeline, Type first, Type second)
    {
        return Reaches(pipeline, first, second) || Reaches(pipeline, second, first);
    }

    // Breadth-first over "runs before" edges between the pipeline's own behaviors.
    private bool Reaches(IReadOnlyList<Type> pipeline, Type from, Type to)
    {
        var visited = new HashSet<Type> { from };
        var pending = new Queue<Type>();
        pending.Enqueue(from);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var next in pipeline)
            {
                if (next == current || !RunsBefore(current, next) || !visited.Add(next))
                {
                    continue;
                }

                if (next == to)
                {
                    return true;
                }

                pending.Enqueue(next);
            }
        }

        return false;
    }

    // A direct constraint, declared by either side: first.Before matches second, or second.After matches first.
    private bool RunsBefore(Type first, Type second)
    {
        return (Find(first)?.Before.Any(before => Matches(second, before)) ?? false) ||
               (Find(second)?.After.Any(after => Matches(first, after)) ?? false);
    }

    // A closed behavior can match two rules: one for itself and one for its open-generic definition (for example the
    // generator declares AuditBehavior<,> in a stage and the application adds a Before for AuditBehavior<X, Y>).
    // Both apply, so they are merged; two different stages are a conflict, not a silent pick.
    private MergedRule? Find(Type type)
    {
        if (_rules.Count == 0)
        {
            return null;
        }

        _rules.TryGetValue(type, out var closed);
        MergedRule? open = null;
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            _rules.TryGetValue(type.GetGenericTypeDefinition(), out open);
        }

        if (closed is null || open is null)
        {
            return closed ?? open;
        }

        if (closed.Stage is { } closedStage && open.Stage is { } openStage && closedStage != openStage)
        {
            var definition = type.GetGenericTypeDefinition();
            throw new PipelineOrderingException(
                $"'{DisplayName(type)}' is declared in the {closedStage} stage, but its definition " +
                $"'{DisplayName(definition)}' is declared in {openStage}. Declare a single stage.",
                false, [type, definition]);
        }

        var merged = new MergedRule { Stage = closed.Stage ?? open.Stage };
        foreach (var before in closed.Before.Concat(open.Before))
        {
            AddDistinct(merged.Before, before);
        }

        foreach (var after in closed.After.Concat(open.After))
        {
            AddDistinct(merged.After, after);
        }

        return merged;
    }

    private Plan CreatePlan<T>(T[] items, Func<T, object> behaviorOf)
    {
        var count = items.Length;
        var types = new Type[count];
        var orders = new uint[count];
        var stages = new PipelineStage[count];
        var rules = new MergedRule?[count];
        for (var i = 0; i < count; i++)
        {
            var behavior = behaviorOf(items[i]);
            types[i] = behavior.GetType();
            orders[i] = PipelineBehaviorOrdering.OrderOf(behavior);
            rules[i] = Find(types[i]);
            stages[i] = rules[i]?.Stage ?? PipelineStages.Of(orders[i]);
        }

        var successors = new List<int>?[count];
        var predecessorCounts = new int[count];

        void Require(int first, int then)
        {
            if (stages[first] < stages[then])
            {
                return;
            }

            if (stages[first] > stages[then])
            {
                throw new PipelineOrderingException(
                    $"'{DisplayName(types[first])}' must run before '{DisplayName(types[then])}', but it runs in " +
                    $"the {stages[first]} stage, after {stages[then]}. Change one of the stages or drop the constraint.",
                    false, [types[first], types[then]]);
            }

            var list = successors[first] ??= [];
            if (!list.Contains(then))
            {
                list.Add(then);
                predecessorCounts[then]++;
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (rules[i] is not { } rule)
            {
                continue;
            }

            for (var j = 0; j < count; j++)
            {
                if (j == i)
                {
                    continue;
                }

                if (rule.Before.Any(before => Matches(types[j], before)))
                {
                    Require(i, j);
                }

                if (rule.After.Any(after => Matches(types[j], after)))
                {
                    Require(j, i);
                }
            }
        }

        var permutation = new int[count];
        var placed = new bool[count];
        for (var position = 0; position < count; position++)
        {
            var next = -1;
            for (var candidate = 0; candidate < count; candidate++)
            {
                if (placed[candidate] || predecessorCounts[candidate] != 0)
                {
                    continue;
                }

                if (next < 0 ||
                    stages[candidate] < stages[next] ||
                    (stages[candidate] == stages[next] && orders[candidate] < orders[next]))
                {
                    next = candidate;
                }
            }

            if (next < 0)
            {
                var cycle = Enumerable.Range(0, count).Where(index => !placed[index]).Select(index => types[index])
                    .ToArray();
                throw new PipelineOrderingException(
                    $"The Before/After constraints of {string.Join(", ", cycle.Select(type => $"'{DisplayName(type)}'"))} " +
                    "form a cycle, so no order satisfies them. Remove one of the constraints.", true, cycle);
            }

            placed[next] = true;
            permutation[position] = next;
            foreach (var successor in successors[next] ?? [])
            {
                predecessorCounts[successor]--;
            }
        }

        return new Plan(types, orders, permutation);
    }

    private static void AddDistinct(List<Type> list, Type? type)
    {
        if (type is not null && !list.Contains(type))
        {
            list.Add(type);
        }
    }

    internal static string DisplayName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name;
        var arity = name.IndexOf('`');
        var baseName = arity < 0 ? name : name[..arity];
        return type.IsGenericTypeDefinition
            ? $"{baseName}<{new string(',', type.GetGenericArguments().Length - 1)}>"
            : $"{baseName}<{string.Join(", ", type.GetGenericArguments().Select(DisplayName))}>";
    }

    private sealed class MergedRule
    {
        public PipelineStage? Stage { get; set; }

        public List<Type> Before { get; } = [];

        public List<Type> After { get; } = [];
    }

    /// <summary>A resolved order, valid while the same behavior types and <c>Order</c>s come back.</summary>
    private sealed class Plan(Type[] types, uint[] orders, int[] permutation)
    {
        public int[] Permutation { get; } = permutation;

        public bool Matches<T>(T[] items, Func<T, object> behaviorOf)
        {
            if (items.Length != types.Length)
            {
                return false;
            }

            for (var i = 0; i < items.Length; i++)
            {
                var behavior = behaviorOf(items[i]);
                if (behavior.GetType() != types[i] || PipelineBehaviorOrdering.OrderOf(behavior) != orders[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
