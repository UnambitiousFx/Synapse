using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class MapSynapseEndpointsTests
{
    [Fact]
    public void MapSynapseEndpoints_WhenTwoEndpointsShareVerbAndRoute_Throws()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new FirstDupBinder());
        EndpointRegistry.RegisterBinder(new SecondDupBinder());
        EndpointRegistry.RegisterMetadata<FirstDupEndpoint>(new EndpointMetadata(["GET"], "/dup"));
        EndpointRegistry.RegisterMetadata<SecondDupEndpoint>(new EndpointMetadata(["GET"], "/dup"));

        var app = WebApplication.CreateSlimBuilder().Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => app.MapSynapseEndpoints(new DupGroup()));
        Assert.Contains("GET /dup", exception.Message);
    }

    [Fact]
    public void MapSynapseEndpoints_WhenRoutesAreDistinct_MapsBothWithoutThrowing()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new HappyFirstBinder());
        EndpointRegistry.RegisterBinder(new HappySecondBinder());
        EndpointRegistry.RegisterMetadata<HappyFirstEndpoint>(new EndpointMetadata(["GET"], "/happy-a"));
        EndpointRegistry.RegisterMetadata<HappySecondEndpoint>(new EndpointMetadata(["GET"], "/happy-b"));

        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        var exception = Record.Exception(() => app.MapSynapseEndpoints(new HappyGroup()));

        // Assert
        Assert.Null(exception);
        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
        Assert.Contains("/happy-a", routes);
        Assert.Contains("/happy-b", routes);
    }

    [Fact]
    public void MapSynapseEndpoints_Always_ReturnsSameBuilderForChaining()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new ChainBinder());
        EndpointRegistry.RegisterMetadata<ChainEndpoint>(new EndpointMetadata(["GET"], "/chain"));

        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        var result = app.MapSynapseEndpoints(new ChainGroup());

        // Assert
        Assert.Same((IEndpointRouteBuilder)app, result);
    }

    [Fact]
    public void MapSynapseEndpoints_WhenAGroupPrefixCreatesTheDuplicate_ThrowsWithThePrefixedRoute()
    {
        // Arrange: two endpoints share a group and declare the identical bare route template.
        // If DataSources enumeration surfaced the bare template instead of the group-prefixed
        // one, the exception message would read "GET /dup" instead of "GET /shared/dup" - this
        // assertion is the proof that group prefixes are present in what the check inspects.
        EndpointRegistry.RegisterBinder(new GroupDupFirstBinder());
        EndpointRegistry.RegisterBinder(new GroupDupSecondBinder());
        EndpointRegistry.RegisterMetadata<GroupDupFirstEndpoint>(
            new EndpointMetadata(["GET"], "/dup", typeof(GroupPrefixDupGroup), static () => new GroupPrefixDupGroup()));
        EndpointRegistry.RegisterMetadata<GroupDupSecondEndpoint>(
            new EndpointMetadata(["GET"], "/dup", typeof(GroupPrefixDupGroup), static () => new GroupPrefixDupGroup()));

        var app = WebApplication.CreateSlimBuilder().Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => app.MapSynapseEndpoints(new GroupPrefixDupEndpointGroup()));
        Assert.Contains("GET /shared/dup", exception.Message);
    }

    private sealed class DupGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<FirstDupEndpoint>();
            endpoints.MapEndpoint<SecondDupEndpoint>();
        }
    }

    private sealed record FirstDupQuery : IRequest<string>;

    private sealed class FirstDupEndpoint : Endpoint<FirstDupQuery, string>;

    private sealed class FirstDupBinder : IEndpointBinder<FirstDupQuery>
    {
        public ValueTask<BindResult<FirstDupQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<FirstDupQuery>.Success(new FirstDupQuery()));
        }
    }

    private sealed record SecondDupQuery : IRequest<string>;

    private sealed class SecondDupEndpoint : Endpoint<SecondDupQuery, string>;

    private sealed class SecondDupBinder : IEndpointBinder<SecondDupQuery>
    {
        public ValueTask<BindResult<SecondDupQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<SecondDupQuery>.Success(new SecondDupQuery()));
        }
    }

    private sealed class HappyGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<HappyFirstEndpoint>();
            endpoints.MapEndpoint<HappySecondEndpoint>();
        }
    }

    private sealed record HappyFirstQuery : IRequest<string>;

    private sealed class HappyFirstEndpoint : Endpoint<HappyFirstQuery, string>;

    private sealed class HappyFirstBinder : IEndpointBinder<HappyFirstQuery>
    {
        public ValueTask<BindResult<HappyFirstQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<HappyFirstQuery>.Success(new HappyFirstQuery()));
        }
    }

    private sealed record HappySecondQuery : IRequest<string>;

    private sealed class HappySecondEndpoint : Endpoint<HappySecondQuery, string>;

    private sealed class HappySecondBinder : IEndpointBinder<HappySecondQuery>
    {
        public ValueTask<BindResult<HappySecondQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<HappySecondQuery>.Success(new HappySecondQuery()));
        }
    }

    private sealed class ChainGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<ChainEndpoint>();
        }
    }

    private sealed record ChainQuery : IRequest<string>;

    private sealed class ChainEndpoint : Endpoint<ChainQuery, string>;

    private sealed class ChainBinder : IEndpointBinder<ChainQuery>
    {
        public ValueTask<BindResult<ChainQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<ChainQuery>.Success(new ChainQuery()));
        }
    }

    private sealed class GroupPrefixDupGroup : EndpointGroup
    {
        public override void Configure(IEndpointGroupBuilder builder)
        {
            builder.Prefix("/shared");
        }
    }

    private sealed class GroupPrefixDupEndpointGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<GroupDupFirstEndpoint>();
            endpoints.MapEndpoint<GroupDupSecondEndpoint>();
        }
    }

    private sealed record GroupDupFirstQuery : IRequest<string>;

    private sealed class GroupDupFirstEndpoint : Endpoint<GroupDupFirstQuery, string>;

    private sealed class GroupDupFirstBinder : IEndpointBinder<GroupDupFirstQuery>
    {
        public ValueTask<BindResult<GroupDupFirstQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<GroupDupFirstQuery>.Success(new GroupDupFirstQuery()));
        }
    }

    private sealed record GroupDupSecondQuery : IRequest<string>;

    private sealed class GroupDupSecondEndpoint : Endpoint<GroupDupSecondQuery, string>;

    private sealed class GroupDupSecondBinder : IEndpointBinder<GroupDupSecondQuery>
    {
        public ValueTask<BindResult<GroupDupSecondQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<GroupDupSecondQuery>.Success(new GroupDupSecondQuery()));
        }
    }
}
