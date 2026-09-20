namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     How serious a <see cref="SynapseValidationIssue" /> is.
/// </summary>
public enum SynapseValidationSeverity
{
    /// <summary>Legal but probably unintended; does not make a report invalid.</summary>
    Warning,

    /// <summary>A misconfiguration that makes the report invalid.</summary>
    Error
}
