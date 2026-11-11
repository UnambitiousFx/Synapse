using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Checks the <c>[PipelineBehavior]</c> ordering declarations the generator can see. A cycle of Before/After
///     constraints (MDG018) and a constraint that contradicts two declared stages, or a behavior ordered relative to
///     itself (MDG019), fail any pipeline that contains the behaviors involved, so they are build errors. What depends
///     on a runtime <c>Order</c> (a stage implied by it) is left to <c>ValidateSynapse</c>.
/// </summary>
internal static class BehaviorOrderingAnalysis
{
    private static readonly DiagnosticDescriptor CycleRule = new(
        "MDG018",
        "Pipeline behavior ordering cycle",
        "The Before/After constraints of {0} form a cycle ({1}), so no pipeline containing them can be ordered. Remove one of the constraints.",
        "Synapse.Generator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor ImpossibleRule = new(
        "MDG019",
        "Impossible pipeline behavior ordering constraint",
        "{0}",
        "Synapse.Generator",
        DiagnosticSeverity.Error,
        true);

    /// <summary>Keeps the first declaration of each behavior (a type can be both attributed and global).</summary>
    public static ImmutableArray<BehaviorOrderingDetail> Deduplicate(IEnumerable<BehaviorOrderingDetail> orderings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<BehaviorOrderingDetail>();
        foreach (var ordering in orderings)
        {
            if (seen.Add(ordering.BehaviorKey))
            {
                result.Add(ordering);
            }
        }

        return result.ToImmutable();
    }

    public static void Report(SourceProductionContext context, ImmutableArray<BehaviorOrderingDetail> orderings)
    {
        if (orderings.IsEmpty)
        {
            return;
        }

        var byKey = orderings.ToDictionary(ordering => ordering.BehaviorKey, StringComparer.Ordinal);
        var successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void AddEdge(string first, string then, BehaviorOrderingDetail declaredBy)
        {
            if (byKey.TryGetValue(first, out var firstDetail) && byKey.TryGetValue(then, out var thenDetail) &&
                firstDetail.HasStage && thenDetail.HasStage && firstDetail.Stage > thenDetail.Stage)
            {
                Report(context, ImpossibleRule, declaredBy,
                    $"'{Display(first)}' must run before '{Display(then)}', but it is declared in the " +
                    $"{firstDetail.StageName ?? firstDetail.Stage.ToString()} stage, after " +
                    $"{thenDetail.StageName ?? thenDetail.Stage.ToString()}. Change one of the stages or drop the constraint.");
                return;
            }

            if (!successors.TryGetValue(first, out var list))
            {
                successors[first] = list = [];
            }

            if (!list.Contains(then))
            {
                list.Add(then);
            }
        }

        foreach (var ordering in orderings)
        {
            if (ordering.BeforeKey is { } before)
            {
                if (before == ordering.BehaviorKey)
                {
                    Report(context, ImpossibleRule, ordering,
                        $"'{Display(ordering.BehaviorKey)}' declares Before = itself. Name another behavior.");
                }
                else
                {
                    AddEdge(ordering.BehaviorKey, before, ordering);
                }
            }

            if (ordering.AfterKey is { } after)
            {
                if (after == ordering.BehaviorKey)
                {
                    Report(context, ImpossibleRule, ordering,
                        $"'{Display(ordering.BehaviorKey)}' declares After = itself. Name another behavior.");
                }
                else
                {
                    AddEdge(after, ordering.BehaviorKey, ordering);
                }
            }
        }

        foreach (var component in StronglyConnectedComponents(successors))
        {
            if (component.Count < 2)
            {
                continue;
            }

            var members = string.Join(", ", component.Select(key => $"'{Display(key)}'"));
            var path = string.Join(" -> ", component.Concat([component[0]]).Select(Display));
            foreach (var key in component)
            {
                if (byKey.TryGetValue(key, out var detail))
                {
                    context.ReportDiagnostic(Diagnostic.Create(CycleRule,
                        detail.Location?.ToLocation() ?? Location.None, members, path));
                }
            }
        }
    }

    private static void Report(SourceProductionContext context, DiagnosticDescriptor rule,
        BehaviorOrderingDetail declaredBy, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(rule, declaredBy.Location?.ToLocation() ?? Location.None,
            message));
    }

    private static string Display(string key)
    {
        return BehaviorOrderingDetail.DisplayName(key);
    }

    // Tarjan's algorithm. Graphs here are a handful of behaviors, so recursion depth is not a concern. Each
    // component is returned in the order its members follow the edges, so it reads as the cycle path.
    private static List<List<string>> StronglyConnectedComponents(Dictionary<string, List<string>> successors)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<List<string>>();

        void Visit(string node)
        {
            indices[node] = lowLinks[node] = index++;
            stack.Push(node);
            onStack.Add(node);

            if (successors.TryGetValue(node, out var next))
            {
                foreach (var successor in next)
                {
                    if (!indices.ContainsKey(successor))
                    {
                        Visit(successor);
                        lowLinks[node] = Math.Min(lowLinks[node], lowLinks[successor]);
                    }
                    else if (onStack.Contains(successor))
                    {
                        lowLinks[node] = Math.Min(lowLinks[node], indices[successor]);
                    }
                }
            }

            if (lowLinks[node] != indices[node])
            {
                return;
            }

            var component = new List<string>();
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            } while (member != node);

            component.Reverse();
            components.Add(component);
        }

        foreach (var node in successors.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList())
        {
            if (!indices.ContainsKey(node))
            {
                Visit(node);
            }
        }

        return components;
    }
}
