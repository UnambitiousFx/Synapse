using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Validation;

internal static class SynapseValidator
{
    public static SynapseValidationReport Validate(SynapseRegistry registry,
        IPipelineDescriber describer,
        Func<PipelineOrdering> resolveOrdering)
    {
        var issues = new List<SynapseValidationIssue>();

        foreach (var request in registry.RequestsWithBehaviors.Where(type =>
                     !registry.RequestHandlerCounts.ContainsKey(type)))
        {
            issues.Add(NoHandler(request));
        }

        foreach (var @event in registry.EventsWithBehaviors.Where(type => !registry.EventsWithHandlers.Contains(type)))
        {
            issues.Add(NoHandler(@event));
        }

        foreach (var (request, count) in registry.RequestHandlerCounts.Where(pair => pair.Value > 1))
        {
            issues.Add(new SynapseValidationIssue("SYN002", SynapseValidationSeverity.Error,
                $"{count} handlers are registered for request '{request}'. Only the last registered one runs; the " +
                "others are silently ignored. Keep one handler per request.", request));
        }

        // Conflicting declarations (one behavior in two stages) surface when the ordering is built, before any
        // pipeline; every pipeline would then fail the same way, so report it once and skip the probes.
        PipelineOrdering ordering;
        try
        {
            ordering = resolveOrdering();
        }
        catch (Exception exception) when (FindOrderingException(exception) is { } orderingException)
        {
            issues.Add(OrderingIssue(orderingException, orderingException.BehaviorTypes[0], "the pipelines"));
            return Report(issues);
        }

        foreach (var (type, probe) in registry.Probes)
        {
            PipelineDescription? description;
            try
            {
                description = probe(describer);
            }
            catch (Exception exception) when (FindOrderingException(exception) is { } orderingException)
            {
                issues.Add(OrderingIssue(orderingException, type, $"the pipeline for '{type}'"));
                continue;
            }
            catch (Exception exception)
            {
                issues.Add(new SynapseValidationIssue("SYN003", SynapseValidationSeverity.Error,
                    $"The pipeline for '{type}' cannot be resolved: {exception.GetType().Name}: {exception.Message}",
                    type));
                continue;
            }

            if (description is null)
            {
                continue;
            }

            foreach (var tie in description.Behaviors.GroupBy(behavior => (behavior.Stage, behavior.Order)))
            {
                var members = tie.ToList();
                if (!IsUserDeclaredTie(members, ordering))
                {
                    continue;
                }

                var names = string.Join(", ", members.Select(behavior => DisplayName(behavior.Type)));
                issues.Add(new SynapseValidationIssue("SYN004", SynapseValidationSeverity.Warning,
                    $"Behaviors {names} of the pipeline for '{type}' share Order {tie.Key.Order} in the " +
                    $"{tie.Key.Stage} stage, so they run in registration order. Give them distinct Order values, " +
                    "or a Before/After constraint, if the order matters.", type));
            }
        }

        return Report(issues);
    }

    private static SynapseValidationReport Report(List<SynapseValidationIssue> issues)
    {
        return new SynapseValidationReport(issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Type.FullName, StringComparer.Ordinal));
    }

    private static SynapseValidationIssue OrderingIssue(PipelineOrderingException exception, Type type, string where)
    {
        return exception.IsCycle
            ? new SynapseValidationIssue("SYN005", SynapseValidationSeverity.Error,
                $"The behaviors of {where} cannot be ordered: {exception.Message}", type)
            : new SynapseValidationIssue("SYN006", SynapseValidationSeverity.Error,
                $"The behaviors of {where} cannot be ordered: {exception.Message}", type);
    }

    // The container may wrap a constructor's exception, depending on how it activates the proxy.
    private static PipelineOrderingException? FindOrderingException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is PipelineOrderingException orderingException)
            {
                return orderingException;
            }

            exception = exception.InnerException;
        }

        return null;
    }

    // Behaviors that declare no Order all report Last, and Synapse's own behaviors are not the user's to reorder,
    // so only a tie between two user behaviors that opted into IOrderedPipelineBehavior is actionable. A tie that a
    // Before/After constraint settles is not decided by registration order, so it is not reported either.
    private static bool IsUserDeclaredTie(List<BehaviorDescription> members, PipelineOrdering ordering)
    {
        var ordered = members.Where(behavior => typeof(IOrderedPipelineBehavior).IsAssignableFrom(behavior.Type))
            .ToList();
        if (ordered.Count < 2 || !ordered.Any(behavior => behavior.Type.Assembly != typeof(SynapseConfig).Assembly))
        {
            return false;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                if (!ordering.AreConstrained(ordered[i].Type, ordered[j].Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string DisplayName(Type type)
    {
        var name = type.Name;
        var arity = name.IndexOf('`');
        return arity < 0 ? name : name[..arity];
    }

    private static SynapseValidationIssue NoHandler(Type type)
    {
        return new SynapseValidationIssue("SYN001", SynapseValidationSeverity.Error,
            $"A pipeline behavior is registered for '{type}' but no handler is registered for it, so the behavior " +
            "never runs. Register the handler or remove the behavior.", type);
    }
}
