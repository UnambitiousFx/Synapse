using System.Security.Claims;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Authorization.Tests;

[TestSubject(typeof(ClaimsCurrentPrincipal))]
public sealed class ClaimsCurrentPrincipalTests
{
    [Fact]
    public void Properties_WithNullUser_AreAnonymous()
    {
        // Arrange (Given)
        var principal = new ClaimsCurrentPrincipal(null);

        // Act / Assert (When / Then)
        Assert.False(principal.IsAuthenticated);
        Assert.Null(principal.Id);
        Assert.False(principal.HasScope("read"));
        Assert.False(principal.IsInRole("admin"));
    }

    [Fact]
    public void IsAuthenticated_WithClaimsButNoAuthenticationType_IsFalse()
    {
        // Arrange (Given) — an identity without an authentication type is not authenticated, whatever its claims
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u-1")]));

        // Act (When)
        var principal = new ClaimsCurrentPrincipal(user);

        // Assert (Then)
        Assert.False(principal.IsAuthenticated);
    }

    [Theory]
    [InlineData("scope", "read write", "write", true)]
    [InlineData("scp", "read  write", "write", true)]
    [InlineData("http://schemas.microsoft.com/identity/claims/scope", "read", "read", true)]
    [InlineData("scope", "read write", "Write", false)]
    [InlineData("scope", "reader", "read", false)]
    [InlineData("other", "read", "read", false)]
    public void HasScope_SplitsSpaceSeparatedScopesCaseSensitively(string claimType,
        string value,
        string scope,
        bool expected)
    {
        // Arrange (Given)
        var principal = new ClaimsCurrentPrincipal(User(new Claim(claimType, value)));

        // Act (When)
        var hasScope = principal.HasScope(scope);

        // Assert (Then)
        Assert.Equal(expected, hasScope);
    }

    [Fact]
    public void HasScope_WithConfiguredClaimTypes_UsesOnlyThem()
    {
        // Arrange (Given)
        var options = new ClaimsCurrentPrincipalOptions { ScopeClaimTypes = ["permissions"] };
        var principal = new ClaimsCurrentPrincipal(
            User(new Claim("permissions", "loans:read"), new Claim("scope", "read")), options);

        // Act / Assert (When / Then)
        Assert.True(principal.HasScope("loans:read"));
        Assert.False(principal.HasScope("read"));
    }

    [Theory]
    [InlineData("sub", "u-1")]
    [InlineData(ClaimTypes.NameIdentifier, "u-2")]
    public void Id_ReadsSubThenNameIdentifier(string claimType, string id)
    {
        // Arrange (Given)
        var principal = new ClaimsCurrentPrincipal(User(new Claim(claimType, id)));

        // Act / Assert (When / Then)
        Assert.Equal(id, principal.Id);
    }

    [Fact]
    public void Id_WithBothClaims_PrefersSub()
    {
        // Arrange (Given)
        var principal = new ClaimsCurrentPrincipal(
            User(new Claim(ClaimTypes.NameIdentifier, "name-id"), new Claim("sub", "sub-id")));

        // Act / Assert (When / Then)
        Assert.Equal("sub-id", principal.Id);
    }

    [Fact]
    public void IsInRole_UsesIdentityRoleClaimTypeAndConfiguredExtras()
    {
        // Arrange (Given)
        var options = new ClaimsCurrentPrincipalOptions { RoleClaimTypes = ["roles"] };
        var principal = new ClaimsCurrentPrincipal(
            User(new Claim(ClaimTypes.Role, "admin"), new Claim("roles", "auditor")), options);

        // Act / Assert (When / Then)
        Assert.True(principal.IsInRole("admin"));
        Assert.True(principal.IsInRole("auditor"));
        Assert.False(principal.IsInRole("owner"));
    }

    [Fact]
    public void IsInRole_WithCustomIdentityRoleClaimType_FollowsIt()
    {
        // Arrange (Given) — the authentication handler set RoleClaimType to "role"
        var identity = new ClaimsIdentity([new Claim("role", "admin"), new Claim(ClaimTypes.Role, "auditor")],
            "Test", ClaimTypes.Name, "role");
        var principal = new ClaimsCurrentPrincipal(new ClaimsPrincipal(identity));

        // Act / Assert (When / Then)
        Assert.True(principal.IsInRole("admin"));
        Assert.False(principal.IsInRole("auditor"));
    }

    [Fact]
    public void Id_WithConfiguredClaimTypes_IgnoresTheDefaults()
    {
        // Arrange (Given)
        var options = new ClaimsCurrentPrincipalOptions { IdClaimTypes = ["oid"] };
        var principal = new ClaimsCurrentPrincipal(
            User(new Claim("sub", "sub-id"), new Claim(ClaimTypes.NameIdentifier, "name-id"), new Claim("oid", "o-1")),
            options);
        var withoutOid = new ClaimsCurrentPrincipal(User(new Claim("sub", "sub-id")), options);

        // Act / Assert (When / Then)
        Assert.Equal("o-1", principal.Id);
        Assert.Null(withoutOid.Id);
    }

    [Fact]
    public void AddSynapseClaimsPrincipal_ResolvesPrincipalFromAccessorPerScope()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var current = User(new Claim("sub", "first"));
        services.AddSynapseClaimsPrincipal(_ => current, o => o.ScopeClaimTypes = ["scope"]);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // Act (When)
        string? first;
        using (var scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<ICurrentPrincipal>().Id;
        }

        current = User(new Claim("sub", "second"));
        using var secondScope = provider.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<ICurrentPrincipal>();

        // Assert (Then)
        Assert.Equal("first", first);
        Assert.Equal("second", second.Id);
        Assert.True(second.IsAuthenticated);
    }

    private static ClaimsPrincipal User(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
