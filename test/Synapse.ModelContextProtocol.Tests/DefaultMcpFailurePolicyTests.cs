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
    public void ToToolError_WithValidationErrorsListingOff_CarriesCodeAndMessage()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true, listValidationErrors: false);
        var failure = new ValidationFailure([new ValidationError("label", "Required.", "request.required")]);

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal($"{FailureCodes.Validation}: {failure.Message}", Text(result));
        Assert.False(result.Meta!.ContainsKey(SynapseMcpMeta.Errors));
    }

    [Fact]
    public void ToToolError_WithValidationFailure_ListsEachErrorWithItsFieldAndCode()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = new ValidationFailure([
            new ValidationError("lines[3].amount", "Must be positive.", "request.amount"),
            new ValidationError("label", "Required."),
            new ValidationError(null, "At least one line.", "request.lines"),
            new ValidationError(null, "Not both.")
        ]) { Code = "orders.invalid_order" };

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal(
            $"""
             orders.invalid_order: {DefaultMcpFailurePolicy.InvalidFieldsMessage}
             - lines[3].amount (request.amount): Must be positive.
             - label: Required.
             - (request.lines): At least one line.
             - Not both.
             """.ReplaceLineEndings("\n"),
            Text(result));
        Assert.Equal("orders.invalid_order", result.Meta![SynapseMcpMeta.FailureCode]!.GetValue<string>());
        Assert.Equal(
            """[{"field":"lines[3].amount","code":"request.amount","message":"Must be positive."},{"field":"label","message":"Required."},{"code":"request.lines","message":"At least one line."},{"message":"Not both."}]""",
            result.Meta[SynapseMcpMeta.Errors]!.ToJsonString());
    }

    [Fact]
    public void ToToolError_WithAggregateHoldingValidationFailures_ListsTheirErrorsUnderTheAggregateCode()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = new AggregateFailure([
            new ConflictFailure("stale"),
            new ValidationFailure([new ValidationError("label", "Required.")]),
            new AggregateFailure([new ValidationFailure([new ValidationError("amount", "Must be positive.")])])
        ]);

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.Equal(
            $"""
             {failure.Code}: {failure.Message}
             - label: Required.
             - amount: Must be positive.
             """.ReplaceLineEndings("\n"),
            Text(result));
        Assert.Equal(2, result.Meta![SynapseMcpMeta.Errors]!.AsArray().Count);
    }

    [Fact]
    public void ToToolError_WithAggregateHoldingValidationAndHiddenFailures_StillHides()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = new AggregateFailure([
            new ValidationFailure([new ValidationError("loanId", "Unknown loan 42.")]),
            new NotFoundFailure("Loan", "42")
        ]);

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.Equal($"{FailureCodes.NotFound}: {DefaultMcpFailurePolicy.HiddenMessage}", Text(result));
        Assert.False(result.Meta!.ContainsKey(SynapseMcpMeta.Errors));
    }

    [Fact]
    public void ToToolError_WithAggregateHoldingValidationAndExceptionalFailures_StillRedacts()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = new AggregateFailure([
            new ValidationFailure([new ValidationError("label", Secret)]),
            Exceptional()
        ]);

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.Equal($"{failure.Code}: {DefaultMcpFailurePolicy.UnexpectedMessage}", Text(result));
        Assert.DoesNotContain(Secret, result.Meta!.ToJsonString());
    }

    [Fact]
    public void ToToolError_WithExceptionalFailure_HidesExceptionMessage()
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);
        var failure = Exceptional();

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal($"{failure.Code}: {DefaultMcpFailurePolicy.UnexpectedMessage}", Text(result));
        Assert.DoesNotContain(Secret, Text(result));
    }

    public static TheoryData<Failure> AggregatesHoldingAnException =>
    [
        new AggregateFailure([Exceptional()]),
        new AggregateFailure([new ValidationFailure("Label is required."), Exceptional()]),
        new AggregateFailure([new ConflictFailure("a"), new AggregateFailure([new BadRequestFailure("b"), Exceptional()])])
    ];

    [Theory]
    [MemberData(nameof(AggregatesHoldingAnException))]
    public void ToToolError_WithAggregateHoldingExceptionalFailure_RedactsTheMessage(Failure failure)
    {
        // Arrange (Given)
        var sut = CreateSut(hideExistence: true);

        // Act (When)
        var result = sut.ToToolError(failure);

        // Assert (Then)
        Assert.True(result.IsError);
        Assert.Equal($"{failure.Code}: {DefaultMcpFailurePolicy.UnexpectedMessage}", Text(result));
        Assert.DoesNotContain(Secret, Text(result));
        Assert.DoesNotContain(Secret, result.Meta!.ToJsonString());
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

    private const string Secret = "Password=hunter2";

    private static ExceptionalFailure Exceptional()
    {
        return new ExceptionalFailure(new InvalidOperationException($"connection string {Secret}"));
    }

    private static DefaultMcpFailurePolicy CreateSut(bool hideExistence,
        bool listValidationErrors = true)
    {
        return new DefaultMcpFailurePolicy(Options.Create(new SynapseMcpOptions
        {
            HideExistence = hideExistence,
            ListValidationErrors = listValidationErrors
        }));
    }

    private static string Text(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
