using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
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
    public void Optional_ForAReferenceType_TreatsAnAbsentValueAsValid_ButStillReportsAnUnparsableOne()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?callback=not-a-url");

        // Act
        var validation = context.Validate();
        var absent = validation.QueryOptional<CallbackUrl>("webhook", out var webhook);
        var unparsable = validation.QueryOptional<CallbackUrl>("callback", out var callback);

        // Assert — the same contract the struct overload has: absent is silent, invalid is reported.
        Assert.True(absent);
        Assert.Null(webhook);
        Assert.False(unparsable);
        Assert.Null(callback);
        Assert.Equal(["callback"], validation.Errors!.Keys);
        Assert.Contains("not a valid", validation.Errors["callback"][0]);
    }

    [Fact]
    public void Optional_ForAReferenceType_ReadsAPresentValueFromEverySource()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.RouteValues["from"] = "https://acme.test/route";
        context.Request.QueryString = new QueryString("?callback=https://acme.test/query");
        context.Request.Headers["X-Callback"] = "https://acme.test/header";

        // Act
        var validation = context.Validate();
        validation.RouteOptional<CallbackUrl>("from", out var fromRoute);
        validation.QueryOptional<CallbackUrl>("callback", out var fromQuery);
        validation.HeaderOptional<CallbackUrl>("X-Callback", out var fromHeader);

        // Assert
        Assert.Equal("https://acme.test/route", fromRoute?.ToString());
        Assert.Equal("https://acme.test/query", fromQuery?.ToString());
        Assert.Equal("https://acme.test/header", fromHeader?.ToString());
        Assert.True(validation.IsValid);
    }

    // ?search= is the most common optional input an HTTP API has, and it went through the collector
    // for the first time here: before, it had to be read off it with TryGetQuery.
    [Fact]
    public void Optional_ForAString_ReadsThePresentValueAndReportsNothingWhenAbsent()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?search=ship");

        // Act
        var validation = context.Validate();
        var found = validation.QueryOptional<string>("search", out var search);
        var absent = validation.QueryOptional<string>("sort", out var sort);

        // Assert
        Assert.True(found);
        Assert.Equal("ship", search);
        Assert.True(absent);
        Assert.Null(sort);
        Assert.True(validation.IsValid);
        Assert.Null(validation.Errors);
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

    [Fact]
    public void QueryValues_WithARepeatedKey_ParsesEveryElement()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?size=1&size=2&size=3");

        // Act
        var validation = context.Validate();
        var read = validation.QueryValues<int>("size", out var sizes);

        // Assert
        Assert.True(read);
        Assert.Equal([1, 2, 3], sizes);
        Assert.True(validation.IsValid);
        Assert.Null(validation.Errors);
    }

    [Fact]
    public void QueryValues_WithAnAbsentKey_YieldsAnEmptyArrayAndReportsNothing()
    {
        // Arrange — HTTP cannot express an empty repeated key, so demanding presence would ask the
        // caller for something they have no way to send. Absent is empty, never a failure.
        var context = new DefaultHttpContext();

        // Act
        var validation = context.Validate();
        var read = validation.QueryValues<int>("size", out var sizes);

        // Assert
        Assert.True(read);
        Assert.Empty(sizes);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void QueryValues_WithOneUnparsableElement_ReportsItsIndexAndKeepsTheRest()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?size=1&size=nope&size=3");

        // Act
        var validation = context.Validate();
        var read = validation.QueryValues<int>("size", out var sizes);

        // Assert — binding continues past a bad element so several bad ones report together.
        Assert.False(read);
        Assert.Equal([1, 3], sizes);
        Assert.NotNull(validation.Errors);
        Assert.Contains("at index 1", Assert.Single(validation.Errors["size"]));
    }

    [Fact]
    public void QueryValues_WithTwoUnparsableElements_ReportsBoth()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?size=nope&size=2&size=nah");

        // Act
        var validation = context.Validate();
        validation.QueryValues<int>("size", out var sizes);

        // Assert
        Assert.Equal([2], sizes);
        Assert.NotNull(validation.Errors);
        Assert.Equal(2, validation.Errors["size"].Length);
    }

    [Fact]
    public void QueryValuesEnum_WithARepeatedKey_ParsesEveryElement()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?day=Monday&day=Friday");

        // Act
        var validation = context.Validate();
        var read = validation.QueryValuesEnum<DayOfWeek>("day", out var days);

        // Assert
        Assert.True(read);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], days);
    }

    [Fact]
    public void HeaderValues_WithARepeatedHeader_ParsesEveryElement()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Size"] = new StringValues(["4", "5"]);

        // Act
        var validation = context.Validate();
        var read = validation.HeaderValues<int>("X-Size", out var sizes);

        // Assert
        Assert.True(read);
        Assert.Equal([4, 5], sizes);
    }
}
