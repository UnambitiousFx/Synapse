using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class EndpointGroupTests
{
    [Fact]
    public void MapEndpoint_WhenEndpointDeclaresAGroup_PrefixesTheRoute()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<GroupedEndpoint>();

        // Assert
        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
        Assert.Contains("/tasks/{id:int}", routes);
    }

    [Fact]
    public void MapEndpoint_WhenTwoEndpointsShareAGroup_ConfigureRunsOnceAndBothRoutesArePrefixed()
    {
        // Arrange
        SharedTasksGroup.ConfigureCallCount = 0;

        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<SharedFirstEndpoint>();
        app.MapEndpoint<SharedSecondEndpoint>();

        // Assert
        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
        Assert.Contains("/shared/first", routes);
        Assert.Contains("/shared/second", routes);
        Assert.Equal(1, SharedTasksGroup.ConfigureCallCount);
    }

    private sealed class TasksGroup : EndpointGroup
    {
        public override void Configure(IEndpointGroupBuilder builder)
        {
            builder.Prefix("/tasks").Tag("Tasks");
        }
    }

    // Carries Id because the route the group prefixes declares {id:int}: the generated binding needs a
    // property to bind it to (SYNE001).
    internal sealed record GroupedQuery : IRequest<string>
    {
        public int Id { get; init; }
    }

    [Get("/{id:int}")]
    [InGroup<TasksGroup>]
    internal sealed partial class GroupedEndpoint : Endpoint<GroupedQuery, string>;

    private sealed class SharedTasksGroup : EndpointGroup
    {
        internal static int ConfigureCallCount;

        public override void Configure(IEndpointGroupBuilder builder)
        {
            ConfigureCallCount++;
            builder.Prefix("/shared").Tag("Shared");
        }
    }

    internal sealed record SharedFirstQuery : IRequest<string>;

    [Get("/first")]
    [InGroup<SharedTasksGroup>]
    internal sealed partial class SharedFirstEndpoint : Endpoint<SharedFirstQuery, string>;

    internal sealed record SharedSecondQuery : IRequest<string>;

    [Get("/second")]
    [InGroup<SharedTasksGroup>]
    internal sealed partial class SharedSecondEndpoint : Endpoint<SharedSecondQuery, string>;
}
