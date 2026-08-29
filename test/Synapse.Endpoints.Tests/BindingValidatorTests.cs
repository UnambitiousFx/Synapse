using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class BindingValidatorTests
{
    [Fact]
    public void Validate_WithNothingReported_IsValidAndAllocatesNoErrorStore()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.RouteValues["id"] = "7";

        // Act
        var validation = context.Validate();
        var read = validation.Route<int>("id", out var id);

        // Assert — Errors staying null is the zero-allocation guarantee the generated binders rely on:
        // a valid request must not pay for a dictionary it never fills.
        Assert.True(read);
        Assert.Equal(7, id);
        Assert.True(validation.IsValid);
        Assert.Null(validation.Errors);
    }

    [Fact]
    public void Validate_AccumulatesEveryProblemRatherThanStoppingAtTheFirst()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?page=nope");

        // Act
        var validation = context.Validate();
        validation.Route<Guid>("taskId", out _);
        validation.Query<int>("page", out _);
        validation.Header<int>("X-Count", out _);

        // Assert
        Assert.False(validation.IsValid);
        Assert.NotNull(validation.Errors);
        Assert.Equal(
            ["X-Count", "page", "taskId"],
            validation.Errors.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Contains("required", validation.Errors["taskId"][0]);
        Assert.Contains("not a valid", validation.Errors["page"][0]);
    }

    [Fact]
    public void Validate_CollectsSeveralMessagesForOneField()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var validation = context.Validate();
        validation.AddError("name", "must not be empty");
        validation.AddError("name", "must be at most 10 characters");

        // Assert
        Assert.Equal(2, validation.Errors!["name"].Length);
    }

    [Fact]
    public void Check_ReportsOnlyWhenTheConditionIsFalse_AndReturnsIt()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var validation = context.Validate();
        var passed = validation.Check(true, "page", "must be at least 1");
        var failed = validation.Check(false, "sort", "must be 'asc' or 'desc'");

        // Assert
        Assert.True(passed);
        Assert.False(failed);
        Assert.Equal(["sort"], validation.Errors!.Keys);
    }

    [Fact]
    public void Optional_TreatsAnAbsentValueAsValid_ButStillReportsAnUnparsableOne()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?limit=abc");

        // Act
        var validation = context.Validate();
        var absent = validation.QueryOptional<int>("page", out var page);
        var unparsable = validation.QueryOptional<int>("limit", out var limit);

        // Assert
        Assert.True(absent);
        Assert.Null(page);
        Assert.False(unparsable);
        Assert.Null(limit);
        Assert.Equal(["limit"], validation.Errors!.Keys);
    }

    [Fact]
    public void Enum_AcceptsBothTheNameAndTheNumericValue()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?byName=Tuesday&byNumber=3&bad=Someday");

        // Act
        var validation = context.Validate();
        var byName = validation.QueryEnum<DayOfWeek>("byName", out var named);
        var byNumber = validation.QueryEnum<DayOfWeek>("byNumber", out var numbered);
        var bad = validation.QueryEnum<DayOfWeek>("bad", out _);

        // Assert
        Assert.True(byName);
        Assert.Equal(DayOfWeek.Tuesday, named);
        Assert.True(byNumber);
        Assert.Equal(DayOfWeek.Wednesday, numbered);
        Assert.False(bad);
    }

    [Fact]
    public async Task Problem_ReturnsA400CarryingEveryFieldsMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        var validation = context.Validate();
        validation.AddError("page", "must be at least 1");
        validation.AddError("sort", "must be 'asc' or 'desc'");

        // Act
        await validation.Problem().ExecuteAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
        var errors = document.RootElement.GetProperty("errors");

        Assert.Equal("must be at least 1", errors.GetProperty("page")[0].GetString());
        Assert.Equal("must be 'asc' or 'desc'", errors.GetProperty("sort")[0].GetString());
    }

    [Fact]
    public void Problem_WithNothingReported_ThrowsRatherThanInventingAnEmptyFailure()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var validation = context.Validate();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => validation.Problem());

        // Assert
        Assert.Contains("Check IsValid", exception.Message);
    }
}
