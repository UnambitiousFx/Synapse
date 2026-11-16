using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Examples.AuthorizationAot;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Authorization;
using UnambitiousFx.Synapse.FluentValidation;

// Exercises every authorization and validation check under NativeAOT, where open generic behaviors cannot be closed at runtime.
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
        o.RequestPolicy<Transfer, TransferPolicy>();
    })
    .AddScoped<IValidator<Positive>, PositiveValidator>()
    .AddSynapseFluentValidation()
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

principal.Set(scopes: "write");
Check("request policy denies", await invoker.InvokeAsync(new Transfer(500)), typeof(UnauthorizedFailure));
Check("request policy allows", await invoker.InvokeAsync(new Transfer(50)), null);

principal.Set(scopes: "read");
Check("denied resource", await invoker.InvokeAsync(new Document()), typeof(UnauthorizedFailure));

principal.Set();
Check("struct query without scope", await invoker.InvokeAsync(new Count()), typeof(UnauthorizedFailure));
Check("stream without scope", await FirstStreamResult(), typeof(UnauthorizedFailure));

principal.Set(scopes: "read");
Check("struct query with scope", await invoker.InvokeAsync(new Count()), null);

principal.Set(scopes: "read");
Check("invalid struct query", await invoker.InvokeAsync(new Positive(0)), typeof(ValidationFailure));
Check("valid struct query", await invoker.InvokeAsync(new Positive(1)), null);

principal.Set(scopes: "write");
var begun = CountingTransactionManager.Begun;
var committed = CountingTransactionManager.Committed;
Check("transactional command", await invoker.InvokeAsync(new Deposit()), null);
CheckScopes("transactional command", 1);
Check("transactional struct command", await invoker.InvokeAsync(new Tally()), null);
CheckScopes("transactional struct command", 2);

// The Type overloads are served from the closed per-type tables, not reflection: check them against the generic ones.
var describer = services.GetRequiredService<IPipelineDescriber>();
CheckDescription("describe struct query by type", describer.Describe(typeof(Count)), describer.Describe<Count, int>());
CheckDescription("describe command by type", describer.Describe(typeof(Deposit)), describer.Describe<Deposit>());
CheckDescription("describe event by type", describer.DescribeEvent(typeof(Pinged)), describer.DescribeEvent<Pinged>());

return failed ? 1 : 0;

void CheckDescription(string name, PipelineDescription? byType, PipelineDescription? generic)
{
    var ok = byType is not null && byType.Equals(generic);
    failed |= !ok;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: {(byType is null ? "no description" : $"{byType.Handlers.Count} handler(s), {byType.Behaviors.Count} behavior(s)")}");
}

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

    public sealed class TransferPolicy : IRequestAccessPolicy<Transfer>
    {
        public ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
            Transfer request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(request.Amount <= 100);
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

    public sealed record Transfer(int Amount) : ICommand<string>;

    public sealed record Rename(string Name) : ICommand<string>;

    public readonly record struct Count : IQuery<int>;

    public readonly record struct Positive(int Value) : IQuery<int>;

    public sealed class PositiveValidator : AbstractValidator<Positive>
    {
        public PositiveValidator()
        {
            RuleFor(x => x.Value).GreaterThan(0);
        }
    }

    [RequestHandler<Positive, int>]
    public sealed class PositiveHandler : IRequestHandler<Positive, int>
    {
        public ValueTask<Result<int>> HandleAsync(Positive request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success(request.Value));
    }

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

    [RequestHandler<Transfer, string>]
    public sealed class TransferHandler : IRequestHandler<Transfer, string>
    {
        public ValueTask<Result<string>> HandleAsync(Transfer request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success("done"));
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
