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
}
