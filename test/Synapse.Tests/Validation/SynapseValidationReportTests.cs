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
        // Arrange (Given)
        var report = new SynapseValidationReport([]);

        // Act (When)
        var isValid = report.IsValid;

        // Assert (Then)
        Assert.True(isValid);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void IsValid_WithOnlyWarnings_IsTrueAndThrowIfInvalidDoesNotThrow()
    {
        // Arrange (Given)
        var report = new SynapseValidationReport([Warning()]);

        // Act (When)
        report.ThrowIfInvalid();

        // Assert (Then)
        Assert.True(report.IsValid);
        Assert.Single(report.Warnings);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void IsValid_WithAnError_IsFalseAndSplitsErrorsFromWarnings()
    {
        // Arrange (Given)
        var report = new SynapseValidationReport([Warning(), Error()]);

        // Act (When)
        var isValid = report.IsValid;

        // Assert (Then)
        Assert.False(isValid);
        Assert.Equal("SYN001", Assert.Single(report.Errors).Code);
        Assert.Equal("SYN004", Assert.Single(report.Warnings).Code);
        Assert.Equal(2, report.Issues.Count);
    }

    [Fact]
    public void ThrowIfInvalid_WithErrors_ThrowsWithTheReportAndListsEveryError()
    {
        // Arrange (Given)
        var report = new SynapseValidationReport([Error("SYN001"), Error("SYN002"), Warning()]);

        // Act (When)
        var exception = Assert.Throws<SynapseValidationException>(report.ThrowIfInvalid);

        // Assert (Then)
        Assert.Same(report, exception.Report);
        Assert.Contains("SYN001", exception.Message);
        Assert.Contains("SYN002", exception.Message);
        Assert.DoesNotContain("SYN004", exception.Message);
    }

    [Fact]
    public void Constructor_WithNullIssues_Throws()
    {
        // Arrange (Given) / Act (When) / Assert (Then)
        Assert.Throws<ArgumentNullException>(() => new SynapseValidationReport(null!));
    }
}
