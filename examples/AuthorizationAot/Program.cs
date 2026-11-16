using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Examples.AuthorizationAot;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Authorization;

// Exercises every authorization check under NativeAOT, where open generic behaviors cannot be closed at runtime.
var principal = new TestPrincipal();
var services = new ServiceCollection()
    .AddSingleton<ICurrentPrincipal>(principal)
    .AddSynapse(cfg =>
    {
        cfg.AddRegisterGroup(new RegisterGroup());
        cfg.AddTransactions<CountingTransactionManager>();
    })
    .AddSynapseAuthorization(o =>
    {
        o.QueryScope = "read";
        o.CommandScope = "write";
        o.Policy<DenyAllPolicy>();
    })
    .BuildServiceProvider();
var invoker = services.GetRequiredService<IInvoker>();

var failed = false;

void Check(string name, IResult result, Type? expectedFailure)
{
    var actual = result.TryGetFailure(out var failure) ? failure.GetType() : null;
    var ok = actual == expectedFailure;
    failed |= !ok;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: expected {expectedFailure?.Name ?? "success"}, got {actual?.Name ?? "success"}");
}

principal.Set(authenticated: false);
Check("anonymous command", await invoker.InvokeAsync(new Anonymous()), typeof(UnauthenticatedFailure));
Check("anonymous command allowed", await invoker.InvokeAsync(new Open()), null);

principal.Set(scopes: "write");
Check("command without role", await invoker.InvokeAsync(new AdminOnly()), typeof(UnauthorizedFailure));

principal.Set(scopes: "read");
Check("command with only the query scope", await invoker.InvokeAsync(new Rename("n")), typeof(UnauthorizedFailure));

principal.Set(scopes: "export");
Check("query with own scope", await invoker.InvokeAsync(new Export()), null);

principal.Set(scopes: "read");
Check("denied resource", await invoker.InvokeAsync(new Document()), typeof(UnauthorizedFailure));

principal.Set();
Check("struct query without scope", await invoker.InvokeAsync(new Count()), typeof(UnauthorizedFailure));
Check("stream without scope", await FirstStreamResult(), typeof(UnauthorizedFailure));

principal.Set(scopes: "read");
Check("struct query with scope", await invoker.InvokeAsync(new Count()), null);

principal.Set(scopes: "write");
var begun = CountingTransactionManager.Begun;
var committed = CountingTransactionManager.Committed;
Check("transactional command", await invoker.InvokeAsync(new Transfer()), null);
CheckScopes("transactional command", 1);
Check("transactional struct command", await invoker.InvokeAsync(new Tally()), null);
CheckScopes("transactional struct command", 2);

return failed ? 1 : 0;

void CheckScopes(string name, int expected)
{
    var ok = CountingTransactionManager.Begun == begun + expected && CountingTransactionManager.Committed == committed + expected;
    failed |= !ok;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: scopes begun/committed {CountingTransactionManager.Begun - begun}/{CountingTransactionManager.Committed - committed}, expected {expected} total");
}
async ValueTask<IResult> FirstStreamResult()
{
    await foreach (var item in invoker.InvokeStreamAsync(new Numbers()))
    {
        return item;
    }

    return Result.Success();
}

namespace UnambitiousFx.Examples.AuthorizationAot
{
    public sealed class TestPrincipal : ICurrentPrincipal
    {
        private HashSet<string> _scopes = [];

        public bool IsAuthenticated { get; private set; }

        public string? Id => IsAuthenticated ? "tester" : null;

        public bool HasScope(string scope) => _scopes.Contains(scope);

        public bool IsInRole(string role) => false;

        public void Set(bool authenticated = true, string? scopes = null)
        {
            IsAuthenticated = authenticated;
            _scopes = scopes is null ? [] : [scopes];
        }
    }

    public sealed class DenyAllPolicy : IResourceAccessPolicy
    {
        public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
            IResourceScoped request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    public sealed record Anonymous : ICommand;

    public sealed record Open : ICommand, IAllowAnonymous;

    public sealed record AdminOnly : ICommand, IRequiresRole
    {
        public string Role => "admin";
    }

    public sealed record Export : IQuery<string>, IRequiresScope
    {
        public string Scope => "export";
    }

    public sealed record Document : IQuery<string>, IResourceScoped
    {
        public string ResourceId => "doc-1";

        public string Access => "read";
    }

    public sealed record Rename(string Name) : ICommand<string>;

    public readonly record struct Count : IQuery<int>;

    public sealed record Numbers : IStreamRequest<int>;

    [RequestHandler<Anonymous>]
    public sealed class AnonymousHandler : IRequestHandler<Anonymous>
    {
        public ValueTask<Result> HandleAsync(Anonymous request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }

    [RequestHandler<Open>]
    public sealed class OpenHandler : IRequestHandler<Open>
    {
        public ValueTask<Result> HandleAsync(Open request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }

    [RequestHandler<AdminOnly>]
    public sealed class AdminOnlyHandler : IRequestHandler<AdminOnly>
    {
        public ValueTask<Result> HandleAsync(AdminOnly request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }

    [RequestHandler<Export, string>]
    public sealed class ExportHandler : IRequestHandler<Export, string>
    {
        public ValueTask<Result<string>> HandleAsync(Export request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success("report"));
    }

    [RequestHandler<Document, string>]
    public sealed class DocumentHandler : IRequestHandler<Document, string>
    {
        public ValueTask<Result<string>> HandleAsync(Document request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success("doc"));
    }

    [RequestHandler<Rename, string>]
    public sealed class RenameHandler : IRequestHandler<Rename, string>
    {
        public ValueTask<Result<string>> HandleAsync(Rename request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success(request.Name));
    }

    [RequestHandler<Count, int>]
    public sealed class CountHandler : IRequestHandler<Count, int>
    {
        public ValueTask<Result<int>> HandleAsync(Count request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success(42));
    }

    [StreamRequestHandler<Numbers, int>]
    public sealed class NumbersHandler : IStreamRequestHandler<Numbers, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(Numbers request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Result.Success(1);
            await Task.CompletedTask;
        }
    }
}
