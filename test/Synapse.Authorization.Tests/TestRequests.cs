using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization.Tests;

public sealed record GetItem(string Id) : IQuery<string>;

public sealed record ExportReport : IQuery<string>, IRequiresScope
{
    public string Scope => "reports:export";
}

public sealed record CreateItem(string Name) : ICommand<string>;

public sealed record RenameItem(string Id, string Name) : ICommand, IResourceScoped
{
    public string ResourceId => Id;
    public string Access => "write";
}

public sealed record PurgeItems : ICommand, IRequiresRole
{
    public string Role => "admin";
}

public sealed record Ping : IRequest;

public sealed record SignIn(string Email) : ICommand, IAllowAnonymous;

public sealed record AnonymousReport : IQuery<string>, IAllowAnonymous, IRequiresScope
{
    public string Scope => "reports:public";
}

public sealed record AnonymousAdminCommand : ICommand, IAllowAnonymous, IRequiresRole
{
    public string Role => "admin";
}

public sealed record BlankScopeQuery(string? Value) : IQuery<string>, IRequiresScope
{
    public string Scope => Value!;
}

public sealed record BlankRoleCommand(string? Value) : ICommand, IRequiresRole
{
    public string Role => Value!;
}

internal sealed class PipelineCalls
{
    public int Count { get; set; }

    public RequestNext<TRequest, Result> Next<TRequest>()
    {
        return new RequestNext<TRequest, Result>((_, _) =>
        {
            Count++;
            return new ValueTask<Result>(Result.Success());
        }, Result.Failure);
    }

    public RequestNext<TRequest, Result<string>> NextWithValue<TRequest>(string value)
    {
        return new RequestNext<TRequest, Result<string>>((_, _) =>
        {
            Count++;
            return new ValueTask<Result<string>>(Result.Success(value));
        }, Result.Failure<string>);
    }
}

internal static class Principals
{
    public static ICurrentPrincipal Anonymous()
    {
        var principal = Substitute.For<ICurrentPrincipal>();
        principal.IsAuthenticated.Returns(false);
        principal.Id.Returns((string?)null);
        return principal;
    }

    public static ICurrentPrincipal Authenticated(string id = "user-1",
        string[]? scopes = null,
        string[]? roles = null)
    {
        var principal = Substitute.For<ICurrentPrincipal>();
        principal.IsAuthenticated.Returns(true);
        principal.Id.Returns(id);
        foreach (var scope in scopes ?? [])
        {
            principal.HasScope(scope).Returns(true);
        }

        foreach (var role in roles ?? [])
        {
            principal.IsInRole(role).Returns(true);
        }

        return principal;
    }
}
