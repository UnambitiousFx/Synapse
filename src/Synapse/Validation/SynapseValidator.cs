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
                $"{count} handlers are registered for request '{request}'. Only the first is used; the others are " +
                "silently ignored. Keep one handler per request.", request));
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

            foreach (var tie in description.Behaviors.GroupBy(behavior => behavior.Order).Where(g => g.Count() > 1))
            {
                var names = string.Join(", ", tie.Select(behavior => behavior.Type.Name));
                issues.Add(new SynapseValidationIssue("SYN004", SynapseValidationSeverity.Warning,
                    $"Behaviors {names} of the pipeline for '{type}' share Order {tie.Key}, so they run in " +
                    "registration order. Give them distinct Order values if the order matters.", type));
            }
        }

        return new SynapseValidationReport(issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Type.FullName, StringComparer.Ordinal));
    }

    private static SynapseValidationIssue NoHandler(Type type)
    {
        return new SynapseValidationIssue("SYN001", SynapseValidationSeverity.Error,
            $"A pipeline behavior is registered for '{type}' but no handler is registered for it, so the behavior " +
            "never runs. Register the handler or remove the behavior.", type);
    }
}
