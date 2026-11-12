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
        var edges = new List<Edge>();

        void AddEdge(string first, string then, EquatableArray<string> binding, BehaviorOrderingDetail declaredBy)
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

            edges.Add(new Edge(first, then, binding));
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
                    AddEdge(ordering.BehaviorKey, before, ordering.BeforeBinding, ordering);
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
                    AddEdge(after, ordering.BehaviorKey, ordering.AfterBinding, ordering);
                }
            }
        }

        foreach (var component in Cycles(edges))
        {
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

    // A constraint edge between behavior definitions. Pipelines lists every pipeline ("kind:message type") of the closed
    // target, the only ones the edge exists in; empty means an open or non-generic target, which applies to every
    // pipeline that has both behaviors.
    private readonly struct Edge
    {
        public Edge(string first, string then, EquatableArray<string> pipelines)
        {
            First = first;
            Then = then;
            Pipelines = pipelines;
        }

        public string First { get; }

        public string Then { get; }

        public EquatableArray<string> Pipelines { get; }

        public bool AppliesTo(string? pipeline)
        {
            return Pipelines.Count == 0 || (pipeline is not null && Pipelines.Contains(pipeline));
        }
    }

    // A cycle only fails a build when one pipeline holds all its edges, so cycles are searched per pipeline: for every
    // pipeline a closed target names (plus one pass for "any pipeline", which only sees unbound edges), over the edges
    // that apply to it. Edges whose pipeline sets are disjoint never meet, so "A before B<X, ...>" and
    // "B before A<Y, ...>" are no cycle, while a typed and a result target of one request, or a target that runs in
    // both a request and a stream pipeline, share the pipeline they have in common.
    private static List<List<string>> Cycles(List<Edge> edges)
    {
        var pipelines = new List<string?> { null };
        pipelines.AddRange(edges.SelectMany(edge => edge.Pipelines).Distinct()
            .OrderBy(pipeline => pipeline, StringComparer.Ordinal));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new List<List<string>>();
        foreach (var pipeline in pipelines)
        {
            var successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var edge in edges.Where(edge => edge.AppliesTo(pipeline)))
            {
                if (!successors.TryGetValue(edge.First, out var list))
                {
                    successors[edge.First] = list = [];
                }

                if (!list.Contains(edge.Then))
                {
                    list.Add(edge.Then);
                }
            }

            foreach (var component in StronglyConnectedComponents(successors).Where(component => component.Count > 1))
            {
                // The same cycle is found again under every pipeline when its edges are all unbound.
                if (seen.Add(string.Join("|", component.OrderBy(key => key, StringComparer.Ordinal))))
                {
                    cycles.Add(component);
                }
            }
        }

        return cycles;
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
