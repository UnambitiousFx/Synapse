using System.Text;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Thrown when the Synapse configuration is invalid.
/// </summary>
public sealed class SynapseValidationException : Exception
{
    /// <summary>
    ///     Creates the exception from an invalid report.
    /// </summary>
    /// <param name="report">The report; its errors form the message.</param>
    public SynapseValidationException(SynapseValidationReport report)
        : base(BuildMessage(report))
    {
        Report = report;
    }

    /// <summary>The full report, warnings included.</summary>
    public SynapseValidationReport Report { get; }

    private static string BuildMessage(SynapseValidationReport report)
    {
        var builder = new StringBuilder("The Synapse configuration is invalid:");
        foreach (var error in report.Errors)
        {
            builder.AppendLine().Append(error.Code).Append(": ").Append(error.Message);
        }

        return builder.ToString();
    }
}
