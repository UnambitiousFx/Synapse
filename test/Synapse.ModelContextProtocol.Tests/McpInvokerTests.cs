using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using Result = UnambitiousFx.Functional.Result;

namespace UnambitiousFx.Synapse.ModelContextProtocol.Tests;

[TestSubject(typeof(McpInvoker))]
public sealed class McpInvokerTests
{
    private static readonly Guid LoanId = Guid.Parse("6f1c2a52-7d4e-4b8a-9a43-1b2f3c4d5e6f");

    [Fact]
    public async Task InvokeAsync_WhenQuerySucceeds_ReturnsJsonFromJsonContext()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new GetLoan(LoanId);
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new Loan(LoanId, "Home", 1200.5m))));
        var sut = CreateSut(invoker, new SynapseMcpOptions { JsonContext = TestJsonContext.Default });

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotEqual(true, result.IsError);
        Assert.Null(result.Meta);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal($$"""{"id":"{{LoanId}}","label":"Home","amount":1200.5}""", text.Text);
    }

    [Fact]
    public async Task InvokeAsync_WhenCommandWithResponseSucceeds_ReturnsJson()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new RenameLoan(LoanId, "Car");
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new Loan(LoanId, "Car", 3m))));
        var sut = CreateSut(invoker, new SynapseMcpOptions { JsonContext = TestJsonContext.Default });

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("\"label\":\"Car\"", text.Text);
    }

    [Fact]
    public async Task InvokeAsync_WithoutJsonContext_SerializesWithMcpDefaults()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new GetLoan(LoanId);
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new Loan(LoanId, "Home", 1m))));
        var sut = CreateSut(invoker, new SynapseMcpOptions());

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal($$"""{"id":"{{LoanId}}","label":"Home","amount":1}""", text.Text);
    }

    [Fact]
    public async Task InvokeAsync_WhenResponseTypeIsMissingFromJsonContext_Throws()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new GetUnregistered();
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new Unregistered("x"))));
        var sut = CreateSut(invoker, new SynapseMcpOptions { JsonContext = TestJsonContext.Default });

        // Act (When)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.InvokeAsync(request, TestContext.Current.CancellationToken));

        // Assert (Then)
        Assert.Contains(nameof(Unregistered), exception.Message);
        Assert.Contains(nameof(SynapseMcpOptions.JsonContext), exception.Message);
    }

    [Fact]
    public async Task InvokeAsync_WhenQueryFails_ReturnsToolErrorFromPolicy()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var policy = Substitute.For<IMcpFailurePolicy>();
        var request = new GetLoan(LoanId);
        var failure = new ConflictFailure("already renamed");
        var expected = new CallToolResult { IsError = true };
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure<Loan>(failure)));
        policy.ToToolError(failure).Returns(expected);
        var sut = CreateSut(invoker, new SynapseMcpOptions(), policy);

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenVoidCommandSucceeds_ReturnsConfirmation()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new DeleteLoan(LoanId);
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));
        var sut = CreateSut(invoker, new SynapseMcpOptions());

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(McpInvoker.VoidSuccessText, text.Text);
    }

    [Fact]
    public async Task InvokeAsync_WhenVoidCommandFails_ReturnsToolErrorFromPolicy()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var policy = Substitute.For<IMcpFailurePolicy>();
        var request = new DeleteLoan(LoanId);
        var failure = new ConflictFailure("still referenced");
        var expected = new CallToolResult { IsError = true };
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure(failure)));
        policy.ToToolError(failure).Returns(expected);
        var sut = CreateSut(invoker, new SynapseMcpOptions(), policy);

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenAnswerExceedsMaxLength_TruncatesAndFlagsIt()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new GetText();
        var answer = new string('a', 100);
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(answer)));
        var sut = CreateSut(invoker,
            new SynapseMcpOptions { JsonContext = TestJsonContext.Default, MaxAnswerLength = 10 });

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(2, result.Content.Count);
        var body = Assert.IsType<TextContentBlock>(result.Content[0]);
        var notice = Assert.IsType<TextContentBlock>(result.Content[1]);
        Assert.Equal("\"aaaaaaaaa", body.Text);
        Assert.Contains("truncated", notice.Text);
        Assert.Contains("10 of 102", notice.Text);
        Assert.NotNull(result.Meta);
        Assert.True(result.Meta[SynapseMcpMeta.Truncated]!.GetValue<bool>());
        Assert.Equal(102, result.Meta[SynapseMcpMeta.OriginalLength]!.GetValue<int>());
    }

    [Fact]
    public async Task InvokeAsync_WhenAnswerFitsMaxLengthExactly_DoesNotTruncate()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new GetText();
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success("abc")));
        var sut = CreateSut(invoker,
            new SynapseMcpOptions { JsonContext = TestJsonContext.Default, MaxAnswerLength = 5 });

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("\"abc\"", text.Text);
        Assert.Null(result.Meta);
    }

    [Fact]
    public async Task InvokeAsync_WhenCutFallsInsideSurrogatePair_KeepsThePairWhole()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var request = new GetRawText();
        // System.Text.Json escapes characters outside the BMP whatever the encoder, so RawText writes the emoji
        // unescaped to put a real surrogate pair across the cut: '"', 'a', high, low.
        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new RawText("a\U0001F600bcdef"))));
        var sut = CreateSut(invoker,
            new SynapseMcpOptions { JsonContext = TestJsonContext.Default, MaxAnswerLength = 3 });

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        var body = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("\"a", body.Text);
    }

    private static McpInvoker CreateSut(IInvoker invoker,
        SynapseMcpOptions options,
        IMcpFailurePolicy? policy = null)
    {
        var wrapped = Options.Create(options);
        return new McpInvoker(invoker, policy ?? new DefaultMcpFailurePolicy(wrapped), wrapped);
    }
}
