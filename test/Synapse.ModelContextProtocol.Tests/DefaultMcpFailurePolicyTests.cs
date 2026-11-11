using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.ModelContextProtocol.Tests;

[TestSubject(typeof(DefaultMcpFailurePolicy))]
public sealed class DefaultMcpFailurePolicyTests
{
    public static TheoryData<Failure> HiddenFailures =>
    [
        new NotFoundFailure("Loan", "42"),
        new UnauthorizedFailure("You may not read loan 42."),
        new AggregateFailure([new BadRequestFailure("bad page"), new UnauthorizedFailure("no")])
    ];

    [Theory]
    [MemberData(nameof(HiddenFailures))]
    public void ToToolError_WithHideExistence_ReturnsSameNotFoundAnswer(Failure failure)
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal($"{FailureCodes.NotFound}: {DefaultMcpFailurePolicy.HiddenMessage}", Text(result));
        Assert.Equal(FailureCodes.NotFound, result.Meta![SynapseMcpMeta.FailureCode]!.GetValue<string>());
    }

    [Fact]
    public void ToToolError_WithHideExistence_NotFoundAndUnauthorizedAreIndistinguishable()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);

        // Act (When)
        var notFound = sut.ToToolError(new NotFoundFailure("Loan", "1"));
        var unauthorized = sut.ToToolError(new UnauthorizedFailure("Forbidden loan 2."));

        // Assert (Then)
        Assert.Equal(Text(notFound), Text(unauthorized));
        Assert.Equal(notFound.Meta!.ToJsonString(), unauthorized.Meta!.ToJsonString());
    }

    [Fact]
    public void ToToolError_WithoutHideExistence_KeepsUnauthorizedCodeAndMessage()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: false);
        var failure = new UnauthorizedFailure("You may not read loan 42.");

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal($"{FailureCodes.Unauthorized}: {failure.Message}", Text(result));
        Assert.Equal(FailureCodes.Unauthorized, result.Meta![SynapseMcpMeta.FailureCode]!.GetValue<string>());
    }

    [Fact]
    public void ToToolError_WithoutHideExistence_KeepsNotFoundMessage()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: false);
        var failure = new NotFoundFailure("Loan", "42");

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.Equal($"{FailureCodes.NotFound}: {failure.Message}", Text(result));
    }

    [Fact]
    public void ToToolError_WithValidationFailure_CarriesCodeAndMessage()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = new ValidationFailure("Label is required.");

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal($"{FailureCodes.Validation}: {failure.Message}", Text(result));
    }

    [Fact]
    public void ToToolError_WithExceptionalFailure_HidesExceptionMessage()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = new ExceptionalFailure(new InvalidOperationException("connection string Password=hunter2"));

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal($"{failure.Code}: {DefaultMcpFailurePolicy.UnexpectedMessage}", Text(result));
        Assert.DoesNotContain("hunter2", Text(result));
    }

    [Fact]
    public void ToToolError_WithAggregateWithoutHiddenFailure_KeepsItsCode()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = new AggregateFailure([new BadRequestFailure("a"), new ConflictFailure("b")]);

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.StartsWith($"{failure.Code}: ", Text(result));
    }

    private static DefaultMcpFailurePolicy CreateSut(bool hideExistence)
    {
        return new DefaultMcpFailurePolicy(Options.Create(new SynapseMcpOptions { HideExistence = hideExistence }));
    }

    private static string Text(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
