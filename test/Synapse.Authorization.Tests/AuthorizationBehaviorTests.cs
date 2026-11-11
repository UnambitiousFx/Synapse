using JetBrains.Annotations;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.Authorization.Tests;

[TestSubject(typeof(ActorAuthorizationBehavior<,>))]
public sealed class ActorAuthorizationBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WithAuthenticatedCaller_CallsNext()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var behavior = new ActorAuthorizationBehavior<Ping, Result>(Principals.Authenticated(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new Ping(), calls.Next<Ping>(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WithAnonymousCaller_ReturnsUnauthenticatedFailureAndRecordsDenial()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var recorder = Substitute.For<IAuthorizationDenialRecorder>();
        var behavior = new ActorAuthorizationBehavior<GetItem, Result<string>>(Principals.Anonymous(), [recorder]);
        var request = new GetItem("42");

        // Act (When)
        var result = await behavior.HandleAsync(request, calls.NextWithValue<GetItem>("item"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthenticatedFailure>(failure);
        Assert.Equal(0, calls.Count);
        await recorder.Received(1).RecordAsync(
            new AuthorizationDenial(request, AuthorizationDenialReason.Unauthenticated, null),
            Arg.Any<CancellationToken>());
    }
}

[TestSubject(typeof(RoleAuthorizationBehavior<,>))]
public sealed class RoleAuthorizationBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WithoutRoleMarker_CallsNext()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var behavior = new RoleAuthorizationBehavior<Ping, Result>(Principals.Authenticated(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new Ping(), calls.Next<Ping>(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WithRequiredRole_CallsNext()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var behavior = new RoleAuthorizationBehavior<PurgeItems, Result>(
            Principals.Authenticated(roles: ["admin"]), []);

        // Act (When)
        var result = await behavior.HandleAsync(new PurgeItems(), calls.Next<PurgeItems>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WithoutRequiredRole_ReturnsUnauthorizedFailureAndRecordsDenial()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var recorder = Substitute.For<IAuthorizationDenialRecorder>();
        var behavior = new RoleAuthorizationBehavior<PurgeItems, Result>(
            Principals.Authenticated(roles: ["user"]), [recorder]);
        var request = new PurgeItems();

        // Act (When)
        var result = await behavior.HandleAsync(request, calls.Next<PurgeItems>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal(0, calls.Count);
        await recorder.Received(1).RecordAsync(
            new AuthorizationDenial(request, AuthorizationDenialReason.MissingRole, "user-1", "admin"),
            Arg.Any<CancellationToken>());
    }
}

[TestSubject(typeof(ScopeAuthorizationBehavior<,>))]
public sealed class ScopeAuthorizationBehaviorTests
{
    [Theory]
    [InlineData("read", true)]
    [InlineData("write", false)]
    public async Task HandleAsync_Query_RequiresQueryScope(string grantedScope, bool expectedSuccess)
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var behavior = new ScopeAuthorizationBehavior<GetItem, Result<string>>(
            Principals.Authenticated(scopes: [grantedScope]), new SynapseAuthorizationOptions(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new GetItem("42"), calls.NextWithValue<GetItem>("item"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.Equal(expectedSuccess ? 1 : 0, calls.Count);
    }

    [Theory]
    [InlineData("write", true)]
    [InlineData("read", false)]
    public async Task HandleAsync_Command_RequiresCommandScope(string grantedScope, bool expectedSuccess)
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var behavior = new ScopeAuthorizationBehavior<PurgeItems, Result>(
            Principals.Authenticated(scopes: [grantedScope]), new SynapseAuthorizationOptions(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new PurgeItems(), calls.Next<PurgeItems>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.Equal(expectedSuccess ? 1 : 0, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_CommandWithResponseWithoutCommandScope_ReturnsUnauthorizedFailure()
    {
        // Arrange (Given) — ICommand<T> is a command too
        var calls = new PipelineCalls();
        var behavior = new ScopeAuthorizationBehavior<CreateItem, Result<string>>(
            Principals.Authenticated(scopes: ["read"]), new SynapseAuthorizationOptions(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new CreateItem("x"), calls.NextWithValue<CreateItem>("id"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal(0, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_PlainRequest_NeedsNoScope()
    {
        // Arrange (Given) — neither a command nor a query
        var calls = new PipelineCalls();
        var behavior = new ScopeAuthorizationBehavior<Ping, Result>(
            Principals.Authenticated(), new SynapseAuthorizationOptions(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new Ping(), calls.Next<Ping>(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WithCustomScope_OverridesDefaultQueryScope()
    {
        // Arrange (Given) — the caller holds the custom scope but not the default "read"
        var calls = new PipelineCalls();
        var behavior = new ScopeAuthorizationBehavior<ExportReport, Result<string>>(
            Principals.Authenticated(scopes: ["reports:export"]), new SynapseAuthorizationOptions(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new ExportReport(), calls.NextWithValue<ExportReport>("report"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WithoutCustomScope_ReturnsUnauthorizedFailureEvenWithDefaultScope()
    {
        // Arrange (Given) — "read" would satisfy the default, not the custom scope
        var calls = new PipelineCalls();
        var recorder = Substitute.For<IAuthorizationDenialRecorder>();
        var behavior = new ScopeAuthorizationBehavior<ExportReport, Result<string>>(
            Principals.Authenticated(scopes: ["read"]), new SynapseAuthorizationOptions(), [recorder]);
        var request = new ExportReport();

        // Act (When)
        var result = await behavior.HandleAsync(request, calls.NextWithValue<ExportReport>("report"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal(0, calls.Count);
        await recorder.Received(1).RecordAsync(
            new AuthorizationDenial(request, AuthorizationDenialReason.MissingScope, "user-1", "reports:export"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithConfiguredQueryScope_RequiresIt()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var options = new SynapseAuthorizationOptions { QueryScope = "items:read" };
        var behavior = new ScopeAuthorizationBehavior<GetItem, Result<string>>(
            Principals.Authenticated(scopes: ["read"]), options, []);

        // Act (When)
        var result = await behavior.HandleAsync(new GetItem("42"), calls.NextWithValue<GetItem>("item"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
    }

    [Fact]
    public async Task HandleAsync_WithDefaultScopesDisabled_LetsCommandsAndQueriesThrough()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var options = new SynapseAuthorizationOptions().DisableDefaultScopes();
        var principal = Principals.Authenticated();
        var query = new ScopeAuthorizationBehavior<GetItem, Result<string>>(principal, options, []);
        var command = new ScopeAuthorizationBehavior<PurgeItems, Result>(principal, options, []);

        // Act (When)
        var queryResult = await query.HandleAsync(new GetItem("42"), calls.NextWithValue<GetItem>("item"),
            TestContext.Current.CancellationToken);
        var commandResult = await command.HandleAsync(new PurgeItems(), calls.Next<PurgeItems>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(queryResult.IsSuccess);
        Assert.True(commandResult.IsSuccess);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WithDefaultScopesDisabled_StillChecksCustomScope()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var options = new SynapseAuthorizationOptions().DisableDefaultScopes();
        var behavior = new ScopeAuthorizationBehavior<ExportReport, Result<string>>(
            Principals.Authenticated(), options, []);

        // Act (When)
        var result = await behavior.HandleAsync(new ExportReport(), calls.NextWithValue<ExportReport>("report"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal(0, calls.Count);
    }
}

[TestSubject(typeof(ResourceAuthorizationBehavior<,>))]
public sealed class ResourceAuthorizationBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WithoutResourceMarker_CallsNextWithoutAskingPolicy()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var policy = Substitute.For<IResourceAccessPolicy>();
        var behavior = new ResourceAuthorizationBehavior<Ping, Result>(Principals.Authenticated(), [], policy);

        // Act (When)
        var result = await behavior.HandleAsync(new Ping(), calls.Next<Ping>(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
        await policy.DidNotReceiveWithAnyArgs().IsAllowedAsync(null!, null!, CancellationToken.None);
    }

    [Fact]
    public async Task HandleAsync_WhenPolicyAllows_CallsNext()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var principal = Principals.Authenticated();
        var request = new RenameItem("item-7", "new name");
        var policy = Substitute.For<IResourceAccessPolicy>();
        policy.IsAllowedAsync(principal, request, Arg.Any<CancellationToken>()).Returns(true);
        var behavior = new ResourceAuthorizationBehavior<RenameItem, Result>(principal, [], policy);

        // Act (When)
        var result = await behavior.HandleAsync(request, calls.Next<RenameItem>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WhenPolicyDenies_ReturnsUnauthorizedFailureAndRecordsDenial()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var recorder = Substitute.For<IAuthorizationDenialRecorder>();
        var policy = Substitute.For<IResourceAccessPolicy>();
        var behavior = new ResourceAuthorizationBehavior<RenameItem, Result>(Principals.Authenticated(),
            [recorder], policy);
        var request = new RenameItem("item-7", "new name");

        // Act (When)
        var result = await behavior.HandleAsync(request, calls.Next<RenameItem>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal(0, calls.Count);
        await recorder.Received(1).RecordAsync(
            new AuthorizationDenial(request, AuthorizationDenialReason.ResourceAccessDenied, "user-1", "write",
                "item-7"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithoutPolicy_DeniesResourceScopedRequest()
    {
        // Arrange (Given) — fails closed
        var calls = new PipelineCalls();
        var behavior = new ResourceAuthorizationBehavior<RenameItem, Result>(Principals.Authenticated(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new RenameItem("item-7", "new name"), calls.Next<RenameItem>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Contains(nameof(IResourceAccessPolicy), failure.Message);
        Assert.Equal(0, calls.Count);
    }

    [Fact]
    public async Task HandleAsync_WithSeveralRecorders_CallsEveryRecorder()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var first = Substitute.For<IAuthorizationDenialRecorder>();
        var second = Substitute.For<IAuthorizationDenialRecorder>();
        var behavior = new ResourceAuthorizationBehavior<RenameItem, Result>(Principals.Authenticated(),
            [first, second], Substitute.For<IResourceAccessPolicy>());

        // Act (When)
        await behavior.HandleAsync(new RenameItem("item-7", "new name"), calls.Next<RenameItem>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        await first.ReceivedWithAnyArgs(1).RecordAsync(null!, CancellationToken.None);
        await second.ReceivedWithAnyArgs(1).RecordAsync(null!, CancellationToken.None);
    }
}
