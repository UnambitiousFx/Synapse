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
