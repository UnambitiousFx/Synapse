using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers the three form diagnostics: SYNE017 (a form property on a bodyless verb), SYNE018 (one
///     message binding from both the form and the JSON body) and SYNE019 (a message that became
///     form-bound purely by inference). Each gets a test that it fires and one that it stays silent.
/// </summary>
public sealed class FormDiagnosticTests
{
    [Theory]
    [InlineData("Get", "SYNE017")]
    [InlineData("Post", null)]
    public void Generate_ForAFormPropertyOnAVerb_ReportsSyne017OnlyWhenTheVerbIsBodyless(
        string verb, string? expected)
    {
        // Arrange
        var source = $$"""
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed record ThingCommand : IRequest
                       {
                           [FromForm] public string Caption { get; init; } = "";
                       }

                       [{{verb}}("/things")]
                       public sealed class ThingEndpoint : Endpoint<ThingCommand>;
                       """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        if (expected is null)
        {
            Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE017");
            return;
        }

        var reported = Assert.Single(diagnostics, d => d.Id == expected);
        Assert.Contains("GET", reported.GetMessage());
    }

    [Fact]
    public void Generate_ForABareFileTypedPropertyOnAGetEndpoint_ReportsSyne017()
    {
        // Arrange — no [FromForm] anywhere on this message: rule 3 resolves File to the form
        // purely because IFormFile is a file type, ahead of (and independent of) the bodyless-verb
        // check. This is not a stray/explicit-only case — a GET genuinely cannot carry this file
        // either way, so SYNE017 must still fire here even though there is no attribute to remove.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingQuery : IRequest
                              {
                                  public IFormFile File { get; init; } = null!;
                              }

                              [Get("/things")]
                              public sealed class ThingEndpoint : Endpoint<ThingQuery>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE017");
        Assert.Contains("GET", reported.GetMessage());
    }

    [Fact]
    public void Generate_WhenOneMessageBindsFromBothTheFormAndTheBody_ReportsSyne018()
    {
        // Arrange — a request is a form or it is JSON, so one of these two can never bind.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  [FromForm] public string Caption { get; init; } = "";
                                  [FromBody] public string Note { get; init; } = "";
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE018");
        Assert.Contains("Caption", reported.GetMessage());
        Assert.Contains("Note", reported.GetMessage());
    }

    [Fact]
    public void Generate_ForAFormOnlyMessage_DoesNotReportSyne018()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  [FromForm] public string Caption { get; init; } = "";
                                  public string Note { get; init; } = "";
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert — Note followed the form under rule 6 rather than staying on the body.
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE018");
    }

    [Fact]
    public void Generate_WhenAFileAloneMakesAMessageFormBound_ReportsSyne019NamingWhatMoved()
    {
        // Arrange — adding a file to an existing JSON message relocates every other property.
        // That is a real behaviour change, and this is its only signal.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  public IFormFile File { get; init; } = null!;
                                  public string Caption { get; init; } = "";
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE019");
        Assert.Equal(DiagnosticSeverity.Info, reported.Severity);
        Assert.Contains("Caption", reported.GetMessage());
    }

    [Fact]
    public void Generate_WhenTheFileCarriesFromForm_DoesNotReportSyne019()
    {
        // Arrange — the annotation states the intent, so there is nothing left to point out.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  [FromForm] public IFormFile File { get; init; } = null!;
                                  public string Caption { get; init; } = "";
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE019");
    }

    [Fact]
    public void Generate_WhenTheFileIsTheOnlyProperty_DoesNotReportSyne019()
    {
        // Arrange — nothing moved, so there is nothing to warn about.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  public IFormFile File { get; init; } = null!;
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE019");
    }
}
