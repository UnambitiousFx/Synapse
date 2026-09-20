using JetBrains.Annotations;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Validation;

[TestSubject(typeof(SynapseValidationReport))]
public sealed class SynapseValidationReportTests
{
    private static SynapseValidationIssue Error(string code = "SYN001") =>
        new(code, SynapseValidationSeverity.Error, "an error", typeof(string));

    private static SynapseValidationIssue Warning(string code = "SYN004") =>
        new(code, SynapseValidationSeverity.Warning, "a warning", typeof(int));

    [Fact]
    public void IsValid_WithNoIssues_IsTrue()
    {
        // Arrange
        var report = new SynapseValidationReport([]);

        // Act
        var isValid = report.IsValid;

        // Assert
        Assert.True(isValid);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void IsValid_WithOnlyWarnings_IsTrueAndThrowIfInvalidDoesNotThrow()
    {
        // Arrange
        var report = new SynapseValidationReport([Warning()]);

        // Act
        report.ThrowIfInvalid();

        // Assert
        Assert.True(report.IsValid);
        Assert.Single(report.Warnings);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void IsValid_WithAnError_IsFalseAndSplitsErrorsFromWarnings()
    {
        // Arrange
        var report = new SynapseValidationReport([Warning(), Error()]);

        // Act
        var isValid = report.IsValid;

        // Assert
        Assert.False(isValid);
        Assert.Equal("SYN001", Assert.Single(report.Errors).Code);
        Assert.Equal("SYN004", Assert.Single(report.Warnings).Code);
        Assert.Equal(2, report.Issues.Count);
    }

    [Fact]
    public void ThrowIfInvalid_WithErrors_ThrowsWithTheReportAndListsEveryError()
    {
        // Arrange
        var report = new SynapseValidationReport([Error("SYN001"), Error("SYN002"), Warning()]);

        // Act
        var exception = Assert.Throws<SynapseValidationException>(report.ThrowIfInvalid);

        // Assert
        Assert.Same(report, exception.Report);
        Assert.Contains("SYN001", exception.Message);
        Assert.Contains("SYN002", exception.Message);
        Assert.DoesNotContain("SYN004", exception.Message);
    }

    [Fact]
    public void Constructor_WithNullIssues_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SynapseValidationReport(null!));
    }
}
