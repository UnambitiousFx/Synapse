using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class MapSynapseEndpointsTests
{
    [Fact]
    public void MapSynapseEndpoints_WhenTwoEndpointsShareVerbAndRoute_Throws()
    {
        // Arrange
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

    // The check used to walk every RouteEndpoint in the table with no filter, so two hand-written
    // MapGet calls on the same route were reported as "More than one Synapse endpoint claims…" —
    // blaming Synapse for routes it never mapped, and (worse) throwing at startup for templates
    // legitimately duplicated and disambiguated by a matcher policy such as API versioning. It is now
    // restricted to endpoints carrying Synapse's own marker metadata.
    [Fact]
    public void MapSynapseEndpoints_WhenTwoHandWrittenRoutesCollide_DoesNotBlameSynapse()
    {
        // Arrange — the duplicate is entirely outside Synapse: two plain MapGet calls on one route,
        // plus one unrelated Synapse endpoint so the check actually has something of ours to inspect.
        EndpointRegistry.RegisterMetadata<ForeignDupEndpoint>(new EndpointMetadata(["GET"], "/mine"));

        var app = WebApplication.CreateSlimBuilder().Build();

        // Mapped through a helper called twice, rather than two literal MapGet calls in this method:
        // ASP0022 flags the latter at compile time, which is exactly the sort of duplicate ASP.NET
        // itself is responsible for reporting — and exactly what this check must stop claiming as its
        // own.
        MapHealth(app);
        MapHealth(app);

        // Act
        var exception = Record.Exception(() => app.MapSynapseEndpoints(new ForeignDupGroup()));

        // Assert
        Assert.Null(exception);
    }

    // The other half: a real Synapse duplicate must still throw even when non-Synapse routes are
    // present, so the filter cannot be mistaken for having disabled the check.
    [Fact]
    public void MapSynapseEndpoints_WhenSynapseEndpointsCollideAlongsideHandWrittenRoutes_StillThrows()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<MixedDupFirstEndpoint>(new EndpointMetadata(["GET"], "/mixed-dup"));
        EndpointRegistry.RegisterMetadata<MixedDupSecondEndpoint>(new EndpointMetadata(["GET"], "/mixed-dup"));

        var app = WebApplication.CreateSlimBuilder().Build();
        MapHealth(app);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => app.MapSynapseEndpoints(new MixedDupGroup()));
        Assert.Contains("GET /mixed-dup", exception.Message);
    }

    private static void MapHealth(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => "ok");
    }

    private sealed class ForeignDupGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<ForeignDupEndpoint>();
        }
    }

    internal sealed record ForeignDupQuery : IRequest<string>;

    internal sealed partial class ForeignDupEndpoint : Endpoint<ForeignDupQuery, string>;

    private sealed class MixedDupGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<MixedDupFirstEndpoint>();
            endpoints.MapEndpoint<MixedDupSecondEndpoint>();
        }
    }

    internal sealed record MixedDupFirstQuery : IRequest<string>;

    internal sealed partial class MixedDupFirstEndpoint : Endpoint<MixedDupFirstQuery, string>;

    internal sealed record MixedDupSecondQuery : IRequest<string>;

    internal sealed partial class MixedDupSecondEndpoint : Endpoint<MixedDupSecondQuery, string>;

    private sealed class DupGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<FirstDupEndpoint>();
            endpoints.MapEndpoint<SecondDupEndpoint>();
        }
    }

    internal sealed record FirstDupQuery : IRequest<string>;

    internal sealed partial class FirstDupEndpoint : Endpoint<FirstDupQuery, string>;

    internal sealed record SecondDupQuery : IRequest<string>;

    internal sealed partial class SecondDupEndpoint : Endpoint<SecondDupQuery, string>;

    private sealed class HappyGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<HappyFirstEndpoint>();
            endpoints.MapEndpoint<HappySecondEndpoint>();
        }
    }

    internal sealed record HappyFirstQuery : IRequest<string>;

    internal sealed partial class HappyFirstEndpoint : Endpoint<HappyFirstQuery, string>;

    internal sealed record HappySecondQuery : IRequest<string>;

    internal sealed partial class HappySecondEndpoint : Endpoint<HappySecondQuery, string>;

    private sealed class ChainGroup : IEndpointGroup
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapEndpoint<ChainEndpoint>();
        }
    }

    internal sealed record ChainQuery : IRequest<string>;

    internal sealed partial class ChainEndpoint : Endpoint<ChainQuery, string>;

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

    internal sealed record GroupDupFirstQuery : IRequest<string>;

    internal sealed partial class GroupDupFirstEndpoint : Endpoint<GroupDupFirstQuery, string>;

    internal sealed record GroupDupSecondQuery : IRequest<string>;

    internal sealed partial class GroupDupSecondEndpoint : Endpoint<GroupDupSecondQuery, string>;
}
