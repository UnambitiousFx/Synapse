using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Validation;

internal static class SynapseValidator
{
    public static SynapseValidationReport Validate(SynapseRegistry registry, IPipelineDescriber describer)
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

        foreach (var (type, probe) in registry.Probes)
        {
            PipelineDescription? description;
            try
            {
                description = probe(describer);
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

            foreach (var tie in description.Behaviors.GroupBy(behavior => behavior.Order))
            {
                var members = tie.ToList();
                if (!IsUserDeclaredTie(members))
                {
                    continue;
                }

                var names = string.Join(", ", members.Select(behavior => DisplayName(behavior.Type)));
                issues.Add(new SynapseValidationIssue("SYN004", SynapseValidationSeverity.Warning,
                    $"Behaviors {names} of the pipeline for '{type}' share Order {tie.Key}, so they run in " +
                    "registration order. Give them distinct Order values if the order matters.", type));
            }
        }

        return new SynapseValidationReport(issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Type.FullName, StringComparer.Ordinal));
    }

    // Behaviors that declare no Order all report Last, and Synapse's own behaviors are not the user's to reorder,
    // so only a tie between two user behaviors that opted into IOrderedPipelineBehavior is actionable.
    private static bool IsUserDeclaredTie(List<BehaviorDescription> members)
    {
        if (members.Count < 2 ||
            members.Count(behavior => typeof(IOrderedPipelineBehavior).IsAssignableFrom(behavior.Type)) < 2)
        {
            return false;
        }

        return members.Any(behavior => behavior.Type.Assembly != typeof(SynapseConfig).Assembly);
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
