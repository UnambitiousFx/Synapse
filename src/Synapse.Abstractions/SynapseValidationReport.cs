using System.Text;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The outcome of <c>ValidateSynapse</c>. Only errors make it invalid; warnings do not.
/// </summary>
public sealed class SynapseValidationReport
{
    /// <summary>
    ///     Creates a report from a list of findings.
    /// </summary>
    /// <param name="issues">The findings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="issues" /> is <c>null</c>.</exception>
    public SynapseValidationReport(IEnumerable<SynapseValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToArray();
        Errors = Issues.Where(issue => issue.Severity == SynapseValidationSeverity.Error).ToArray();
        Warnings = Issues.Where(issue => issue.Severity == SynapseValidationSeverity.Warning).ToArray();
    }

    /// <summary>Every finding.</summary>
    public IReadOnlyList<SynapseValidationIssue> Issues { get; }

    /// <summary>The findings that make the report invalid.</summary>
    public IReadOnlyList<SynapseValidationIssue> Errors { get; }

    /// <summary>The findings that are legal but suspicious.</summary>
    public IReadOnlyList<SynapseValidationIssue> Warnings { get; }

    /// <summary><c>true</c> when there are no errors. Warnings do not count.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    ///     Throws when the report has errors.
    /// </summary>
    /// <exception cref="SynapseValidationException">The report has at least one error.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new SynapseValidationException(this);
        }
    }

    /// <summary>
    ///     Lists every finding, one per line.
    /// </summary>
    /// <returns>The findings as text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var issue in Issues)
        {
            builder.Append(issue.Severity).Append(' ').Append(issue.Code).Append(": ").AppendLine(issue.Message);
        }

        return builder.ToString();
    }
}
