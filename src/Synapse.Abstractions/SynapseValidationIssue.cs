namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     One finding of <c>ValidateSynapse</c>.
/// </summary>
/// <param name="Code">A stable identifier such as <c>SYN001</c>.</param>
/// <param name="Severity">Whether the finding is an error or a warning.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
/// <param name="Type">The request or event type the finding is about.</param>
public sealed record SynapseValidationIssue(
    string Code,
    SynapseValidationSeverity Severity,
    string Message,
    Type Type);
