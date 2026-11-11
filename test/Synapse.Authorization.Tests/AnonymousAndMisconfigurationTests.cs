using JetBrains.Annotations;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.Authorization.Tests;

[TestSubject(typeof(IAllowAnonymous))]
public sealed class AllowAnonymousTests
{
    [Fact]
    public async Task Actor_WithAnonymousCallerAndAllowAnonymous_CallsNext()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var recorder = Substitute.For<IAuthorizationDenialRecorder>();
        var behavior = new ActorAuthorizationBehavior<SignIn, Result>(Principals.Anonymous(), [recorder]);

        // Act (When)
        var result = await behavior.HandleAsync(new SignIn("a@b.c"), calls.Next<SignIn>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
        await recorder.DidNotReceiveWithAnyArgs().RecordAsync(null!, CancellationToken.None);
    }

    [Fact]
    public async Task Scope_AllowAnonymousCommand_SkipsTheDefaultCommandScope()
    {
        // Arrange (Given) — an anonymous caller holds no "write" scope
        var calls = new PipelineCalls();
        var behavior = new ScopeAuthorizationBehavior<SignIn, Result>(Principals.Anonymous(),
            new SynapseAuthorizationOptions(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new SignIn("a@b.c"), calls.Next<SignIn>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task Scope_AllowAnonymousWithOwnScope_StillChecksIt()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var behavior = new ScopeAuthorizationBehavior<AnonymousReport, Result<string>>(Principals.Anonymous(),
            new SynapseAuthorizationOptions(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new AnonymousReport(), calls.NextWithValue<AnonymousReport>("r"),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal(0, calls.Count);
    }

    [Fact]
    public async Task Role_AllowAnonymousWithRole_StillChecksIt()
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var behavior = new RoleAuthorizationBehavior<AnonymousAdminCommand, Result>(Principals.Anonymous(), []);

        // Act (When)
        var result = await behavior.HandleAsync(new AnonymousAdminCommand(), calls.Next<AnonymousAdminCommand>(),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.IsType<UnauthorizedFailure>(failure);
        Assert.Equal(0, calls.Count);
    }
}

public sealed class BlankRequirementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Scope_WithBlankRequiredScope_ThrowsNamingTheRequestType(string? scope)
    {
        // Arrange (Given) — the caller even holds the blank scope, which must not let the request through
        var calls = new PipelineCalls();
        var principal = Principals.Authenticated(scopes: [scope ?? "", "read"]);
        var behavior = new ScopeAuthorizationBehavior<BlankScopeQuery, Result<string>>(principal,
            new SynapseAuthorizationOptions(), []);

        // Act (When)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.HandleAsync(new BlankScopeQuery(scope), calls.NextWithValue<BlankScopeQuery>("x"),
                TestContext.Current.CancellationToken));

        // Assert (Then)
        Assert.Contains(typeof(BlankScopeQuery).FullName!, exception.Message);
        Assert.Contains(nameof(IRequiresScope), exception.Message);
        Assert.Equal(0, calls.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Role_WithBlankRequiredRole_ThrowsNamingTheRequestType(string? role)
    {
        // Arrange (Given)
        var calls = new PipelineCalls();
        var principal = Principals.Authenticated(roles: [role ?? "", "admin"]);
        var behavior = new RoleAuthorizationBehavior<BlankRoleCommand, Result>(principal, []);

        // Act (When)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.HandleAsync(new BlankRoleCommand(role), calls.Next<BlankRoleCommand>(),
                TestContext.Current.CancellationToken));

        // Assert (Then)
        Assert.Contains(typeof(BlankRoleCommand).FullName!, exception.Message);
        Assert.Contains(nameof(IRequiresRole), exception.Message);
        Assert.Equal(0, calls.Count);
    }
}
