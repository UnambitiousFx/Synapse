using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class EndpointGroupTests
{
    [Fact]
    public void MapEndpoint_WhenEndpointDeclaresAGroup_PrefixesTheRoute()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<GroupedEndpoint>(
            new EndpointMetadata(["GET"], "/{id:int}", typeof(TasksGroup), static () => new TasksGroup()));

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
        EndpointRegistry.RegisterMetadata<SharedFirstEndpoint>(
            new EndpointMetadata(["GET"], "/first", typeof(SharedTasksGroup), static () => new SharedTasksGroup()));
        EndpointRegistry.RegisterMetadata<SharedSecondEndpoint>(
            new EndpointMetadata(["GET"], "/second", typeof(SharedTasksGroup), static () => new SharedTasksGroup()));

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

    [Fact]
    public void MapEndpoint_WhenGroupTypeIsSetWithNoFactory_ThrowsActionableException()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<NoFactoryEndpoint>(
            new EndpointMetadata(["GET"], "/{id:int}", typeof(TasksGroup)));

        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        var exception = Record.Exception(() => app.MapEndpoint<NoFactoryEndpoint>());

        // Assert
        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(nameof(NoFactoryEndpoint), invalidOperationException.Message);
        Assert.Contains(nameof(TasksGroup), invalidOperationException.Message);
    }

    private sealed class TasksGroup : EndpointGroup
    {
        public override void Configure(IEndpointGroupBuilder builder)
        {
            builder.Prefix("/tasks").Tag("Tasks");
        }
    }

    internal sealed record GroupedQuery : IRequest<string>;

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

    internal sealed partial class SharedFirstEndpoint : Endpoint<SharedFirstQuery, string>;

    internal sealed record SharedSecondQuery : IRequest<string>;

    internal sealed partial class SharedSecondEndpoint : Endpoint<SharedSecondQuery, string>;

    internal sealed record NoFactoryQuery : IRequest<string>;

    internal sealed partial class NoFactoryEndpoint : Endpoint<NoFactoryQuery, string>;
}
