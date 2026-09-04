namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers binding from the request form: an explicit [FromForm] field, the rule-6 flip that makes
///     the rest of the message form-bound, and the body read and BodyKind the binder emits for it.
/// </summary>
public sealed class FormBinderEmissionTests
{
    private const string FormMessage = """
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

    [Fact]
    public void Generate_ForAFromFormProperty_ReadsTheFormRatherThanAJsonBody()
    {
        // Act
        var generated = GeneratorHarness.GetFile(FormMessage, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("BindingHelpers.ReadFormAsync(context)", generated);
        Assert.DoesNotContain("ReadJsonBodyAsync", generated);
        Assert.Contains("TryGetForm(context, \"Caption\", out var rawCaption)", generated);
        GeneratorHarness.AssertGeneratedCompiles(FormMessage);
    }

    [Fact]
    public void Generate_ForAFormBoundMessage_ResolvesUnannotatedPropertiesToTheFormToo()
    {
        // Assert — a request is a form or it is JSON, never both, so rule 6 follows the form.
        var generated = GeneratorHarness.GetFile(FormMessage, "SynapseEndpointBinders.g.cs");

        Assert.Contains("TryGetForm(context, \"Note\", out var rawNote)", generated);
    }

    [Fact]
    public void Generate_ForAFormBoundMessage_DeclaresAFormBodyKind()
    {
        // Assert
        var generated = GeneratorHarness.GetFile(FormMessage, "SynapseEndpointBinders.g.cs");

        Assert.Contains("BodyKind => global::UnambitiousFx.Synapse.Endpoints.Binding.RequestBodyKind.Form;", generated);
    }

    [Fact]
    public void Generate_ForAFormBoundMessage_ConstructsTheMessageItself()
    {
        // Assert — nothing deserialized it, so the binder builds it exactly as a bodyless one does.
        var generated = GeneratorHarness.GetFile(FormMessage, "SynapseEndpointBinders.g.cs");

        Assert.Contains("var message = new global::TestNs.UploadCommand()", generated);
    }

    [Fact]
    public void Generate_ForAFormMessageWithARouteParameter_StillBindsItFromTheRoute()
    {
        // Arrange
        const string source = """
                              using System;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  public Guid TaskId { get; init; }
                                  [FromForm] public string Caption { get; init; } = "";
                              }

                              [Post("/tasks/{taskId:guid}/attachments")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — rule 4 still precedes the form.
        Assert.Contains("TryGetRoute(context, \"taskId\", out var rawTaskId)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAPrivateFromFormProperty_StillResolvesUnannotatedPropertiesToJson()
    {
        // Arrange — the trap: a private [FromForm] property is invisible to the main binding pass
        // (accessibility excludes it from ever becoming a bound property), so it must be equally
        // invisible to the rule-6 pre-pass that decides whether the rest of the message flips to
        // the form. A pre-pass with a looser filter than the main pass would flip Title to Form
        // even though nothing the main pass can see is actually form-bound.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateCommand : IRequest
                              {
                                  [FromForm] private string Secret { get; init; } = "";
                                  public string Title { get; init; } = "";
                              }

                              [Post("/things")]
                              public sealed class CreateEndpoint : Endpoint<CreateCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — still a plain JSON message.
        Assert.Contains("ReadJsonBodyAsync", generated);
        Assert.Contains("RequestBodyKind.Json;", generated);
        Assert.DoesNotContain("ReadFormAsync", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAFromFormAttributeWithACustomName_ReadsUnderThatName()
    {
        // Arrange — the project's own [FromForm] is positional: the name comes off
        // ConstructorArguments, not NamedArguments. A reader mismatch would silently return null,
        // fall back to the property name, and every default-name test would still pass.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  [FromForm("photo")] public string Image { get; init; } = "";
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetForm(context, \"photo\", out var rawImage)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAnMvcFromFormAttributeWithACustomName_ReadsUnderThatName()
    {
        // Arrange — MVC's [FromForm] is named-property: the name comes off NamedArguments, not
        // ConstructorArguments (it has no positional constructor at all). A reader mismatch would
        // silently return null, fall back to the property name, and every default-name test would
        // still pass. Split into two namespaces (mirroring
        // Generate_ForTheMvcFromHeaderAttribute_ReadsTheHeaderAndNotTheQueryString in
        // BinderEmissionEdgeCaseTests): the message's own namespace imports MVC but not
        // UnambitiousFx.Synapse.Endpoints, so [FromForm] resolves to MVC's with no CS0104 ambiguity
        // against our own [FromForm] — exactly the using set a message file that does not declare
        // its own endpoint would have.
        const string source = """
                              namespace TestNs
                              {
                                  using Microsoft.AspNetCore.Mvc;
                                  using UnambitiousFx.Synapse.Abstractions;

                                  public sealed record UploadCommand : IRequest
                                  {
                                      [FromForm(Name = "photo")] public string Image { get; init; } = "";
                                  }
                              }

                              namespace TestNs2
                              {
                                  using UnambitiousFx.Synapse.Endpoints;

                                  [Post("/uploads")]
                                  public sealed class UploadEndpoint : Endpoint<TestNs.UploadCommand>;
                              }
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetForm(context, \"photo\", out var rawImage)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAJsonMessage_StillDeclaresAJsonBodyKind()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateCommand : IRequest
                              {
                                  public string Title { get; init; } = "";
                              }

                              [Post("/things")]
                              public sealed class CreateEndpoint : Endpoint<CreateCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("RequestBodyKind.Json;", generated);
        Assert.Contains("ReadJsonBodyAsync", generated);
    }

    [Fact]
    public void Generate_ForAnIFormFileProperty_BindsItFromTheFormWithNoAttribute()
    {
        // Arrange — a file has exactly one place it could come from, so inferring the source is safe.
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
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — and the file drags Caption onto the form with it.
        Assert.Contains("TryGetFormFile(context, \"File\", out var rawFile)", generated);
        Assert.Contains("TryGetForm(context, \"Caption\", out var rawCaption)", generated);
        Assert.Contains("The form file is required.", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForANullableIFormFile_DoesNotReportItAsRequired()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  [FromForm] public IFormFile? File { get; init; }
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain("The form file is required.", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAnIFormFileCollection_TakesEveryFileOnTheRequest()
    {
        // Arrange — IFormFileCollection means all files, matching ASP.NET Core's own binder;
        // IFormFile[] means the files under one field name.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  public IFormFileCollection Files { get; init; } = null!;
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("context.Request.Form.Files", generated);
        Assert.DoesNotContain("GetFormFiles", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAnIFormFileArray_TakesTheFilesUnderThatFieldName()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  [FromForm("page")] public IFormFile[] Pages { get; init; } = [];
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("GetFormFiles(context, \"page\")", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAnIFormFileProperty_NeverReportsSyne008()
    {
        // Arrange — IFormFile is an interface over a buffered stream and can never be registered on
        // a JsonSerializerContext. A form-bound message is never JSON-deserialized, so it is not asked to.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  public IFormFile File { get; init; } = null!;
                              }

                              [JsonSerializable(typeof(string))]
                              public partial class AppJsonContext : JsonSerializerContext;

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }

    [Fact]
    public void Generate_ForAFromBodyIFormFileProperty_StillBindsAsFormNotJson()
    {
        // Arrange — the trap: [FromBody] resolves Source = Body through the attribute switch before
        // rule 3 (the file-type check) is ever consulted, so a naive implementation that records
        // whatever ResolveSource actually returned would flip the whole message onto the JSON path —
        // dropping the file from `bindable` in BinderEmitter (which only reads non-Body properties),
        // never calling FormFileValueReadEmitter for it, and demanding a [JsonSerializable]
        // registration (SYNE008) for a message containing a raw IFormFile, which can never be
        // satisfied. The model's Source must be hardcoded to Form for a file-shaped property
        // regardless of what attribute is written on it, so this stays a form read no matter what.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  [FromBody] public IFormFile File { get; init; } = null!;
                                  public string Caption { get; init; } = "";
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains("BindingHelpers.ReadFormAsync(context)", generated);
        Assert.DoesNotContain("ReadJsonBodyAsync", generated);
        Assert.Contains("BodyKind => global::UnambitiousFx.Synapse.Endpoints.Binding.RequestBodyKind.Form;", generated);
        Assert.Contains("TryGetFormFile(context, \"File\", out var rawFile)", generated);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAnUnassignableIFormFileProperty_ReportsSyne011NotSyne012()
    {
        // Arrange — a file has no parse step, so SYNE012 (no viable TryParse) never applies to it, but
        // the value still has to be assignable, so SYNE011 does. A private setter proves both halves at
        // once.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class UploadCommand : IRequest
                              {
                                  public IFormFile File { get; private set; } = null!;
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE011");
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE012");
    }

    [Fact]
    public void Generate_ForAConcreteListOfIFormFile_Compiles()
    {
        // Arrange — GetFormFiles returns IReadOnlyList<IFormFile>, which has no implicit conversion to
        // List<IFormFile>. The emitter must realise a genuine List<T> (ToList(), not a bare assignment)
        // for this property to compile.
        const string source = """
                              using System.Collections.Generic;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  public List<IFormFile> Pages { get; init; } = [];
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act & Assert
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForANotBoundPropertyOnAFormBoundMessage_ReportsNoSyne015()
    {
        // Arrange — nothing JSON-deserializes a form-bound message, so [NotBound] alone is
        // sufficient, exactly as on a bodyless verb. Keying SYNE015 on "the verb carries a body"
        // rather than "some property actually resolved to BindingSource.Body" would false-positive
        // here, since a form-bound POST carries a body but never reads it as JSON.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UploadCommand : IRequest
                              {
                                  public IFormFile File { get; init; } = null!;

                                  [NotBound]
                                  public string? ModifiedBy { get; init; }
                              }

                              [Post("/uploads")]
                              public sealed class UploadEndpoint : Endpoint<UploadCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE015");
    }
}
