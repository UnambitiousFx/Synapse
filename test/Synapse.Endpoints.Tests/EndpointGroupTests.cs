using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class EndpointGroupTests
{
    [Fact]
    public void MapEndpoint_WhenEndpointDeclaresAGroup_PrefixesTheRoute()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new GroupedBinder());
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
        EndpointRegistry.RegisterBinder(new SharedFirstBinder());
        EndpointRegistry.RegisterBinder(new SharedSecondBinder());
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
        EndpointRegistry.RegisterBinder(new NoFactoryBinder());
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

    private sealed record GroupedQuery : IRequest<string>;

    private sealed class GroupedEndpoint : Endpoint<GroupedQuery, string>;

    private sealed class GroupedBinder : IEndpointBinder<GroupedQuery>
    {
        public ValueTask<BindResult<GroupedQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<GroupedQuery>.Success(new GroupedQuery()));
        }
    }

    private sealed class SharedTasksGroup : EndpointGroup
    {
        internal static int ConfigureCallCount;

        public override void Configure(IEndpointGroupBuilder builder)
        {
            ConfigureCallCount++;
            builder.Prefix("/shared").Tag("Shared");
        }
    }

    private sealed record SharedFirstQuery : IRequest<string>;

    private sealed class SharedFirstEndpoint : Endpoint<SharedFirstQuery, string>;

    private sealed class SharedFirstBinder : IEndpointBinder<SharedFirstQuery>
    {
        public ValueTask<BindResult<SharedFirstQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<SharedFirstQuery>.Success(new SharedFirstQuery()));
        }
    }

    private sealed record SharedSecondQuery : IRequest<string>;

    private sealed class SharedSecondEndpoint : Endpoint<SharedSecondQuery, string>;

    private sealed class SharedSecondBinder : IEndpointBinder<SharedSecondQuery>
    {
        public ValueTask<BindResult<SharedSecondQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<SharedSecondQuery>.Success(new SharedSecondQuery()));
        }
    }

    private sealed record NoFactoryQuery : IRequest<string>;

    private sealed class NoFactoryEndpoint : Endpoint<NoFactoryQuery, string>;

    private sealed class NoFactoryBinder : IEndpointBinder<NoFactoryQuery>
    {
        public ValueTask<BindResult<NoFactoryQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<NoFactoryQuery>.Success(new NoFactoryQuery()));
        }
    }
}
