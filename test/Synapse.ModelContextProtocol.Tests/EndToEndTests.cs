using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using Result = UnambitiousFx.Functional.Result;

namespace UnambitiousFx.Synapse.ModelContextProtocol.Tests;

/// <summary>
///     A real MCP server and client over in-memory pipes, with Synapse and the MCP integration registered in a
///     <see cref="ServiceCollection" />: tool call → IMcpInvoker → pipeline → handler → CallToolResult.
/// </summary>
public sealed class EndToEndTests
{
    private static readonly Guid KnownLoan = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForbiddenLoan = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MissingLoan = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task CallTool_WhenQuerySucceeds_ReturnsJsonAnswer()
    {
        // Arrange (Given)
        await using var harness = await Harness.StartAsync(hideExistence: true);

        // Act (When)
        var result = await harness.Client.CallToolAsync("get_loan",
            new Dictionary<string, object?> { ["id"] = KnownLoan },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal($$"""{"id":"{{KnownLoan}}","label":"Home","amount":250000}""", text.Text);
    }

    [Fact]
    public async Task CallTool_WithHideExistence_MissingAndForbiddenGiveTheSameAnswer()
    {
        // Arrange (Given)
        await using var harness = await Harness.StartAsync(hideExistence: true);

        // Act (When)
        var missing = await harness.Client.CallToolAsync("get_loan",
            new Dictionary<string, object?> { ["id"] = MissingLoan },
            cancellationToken: TestContext.Current.CancellationToken);
        var forbidden = await harness.Client.CallToolAsync("get_loan",
            new Dictionary<string, object?> { ["id"] = ForbiddenLoan },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(missing.IsError);
        Assert.True(forbidden.IsError);
        Assert.Equal(Text(missing), Text(forbidden));
        Assert.Equal($"{FailureCodes.NotFound}: {DefaultMcpFailurePolicy.HiddenMessage}", Text(forbidden));
    }

    [Fact]
    public async Task CallTool_WithoutHideExistence_ForbiddenReportsUnauthorized()
    {
        // Arrange (Given)
        await using var harness = await Harness.StartAsync(hideExistence: false);

        // Act (When)
        var forbidden = await harness.Client.CallToolAsync("get_loan",
            new Dictionary<string, object?> { ["id"] = ForbiddenLoan },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(forbidden.IsError);
        Assert.StartsWith($"{FailureCodes.Unauthorized}: ", Text(forbidden));
    }

    [Fact]
    public async Task CallTool_WhenVoidCommandSucceeds_RunsHandlerAndConfirms()
    {
        // Arrange (Given)
        await using var harness = await Harness.StartAsync(hideExistence: true);

        // Act (When)
        var result = await harness.Client.CallToolAsync("delete_loan",
            new Dictionary<string, object?> { ["id"] = KnownLoan },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(McpInvoker.VoidSuccessText, Text(result));
        Assert.Equal([KnownLoan], harness.Services.GetRequiredService<LoanStore>().Deleted);
    }

    [Fact]
    public async Task CallTool_WithTraceparentInMeta_ContinuesTheClientFlow()
    {
        // Arrange (Given)
        await using var harness = await Harness.StartAsync(hideExistence: true);
        const string traceId = "0af7651916cd43dd8448eb211c80319c";
        var parameters = new CallToolRequestParams
        {
            Name = "delete_loan",
            Arguments = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["id"] = System.Text.Json.JsonSerializer.SerializeToElement(KnownLoan)
            },
            Meta = new JsonObject { ["traceparent"] = $"00-{traceId}-b7ad6b7169203331-01" }
        };

        // Act (When)
        var result = await harness.Client.CallToolAsync(parameters, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotEqual(true, result.IsError);
        Assert.Equal([traceId], harness.Services.GetRequiredService<LoanStore>().TraceIds);
    }

    private static string Text(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    public sealed class LoanStore
    {
        public List<Guid> Deleted { get; } = [];

        public List<string> TraceIds { get; } = [];
    }

    [McpServerToolType]
    public sealed class LoanTools(IMcpInvoker invoker)
    {
        [McpServerTool(Name = "get_loan")]
        [Description("Get one loan.")]
        public ValueTask<CallToolResult> GetLoan(Guid id,
            CancellationToken cancellationToken)
        {
            return invoker.InvokeAsync(new GetLoan(id), cancellationToken);
        }

        [McpServerTool(Name = "delete_loan")]
        [Description("Delete one loan.")]
        public ValueTask<CallToolResult> DeleteLoan(Guid id,
            CancellationToken cancellationToken)
        {
            return invoker.InvokeAsync(new DeleteLoan(id), cancellationToken);
        }
    }

    private sealed class GetLoanHandler : IQueryHandler<GetLoan, Loan>
    {
        public ValueTask<Result<Loan>> HandleAsync(GetLoan request,
            CancellationToken cancellationToken = default)
        {
            if (request.Id == KnownLoan)
            {
                return new ValueTask<Result<Loan>>(Result.Success(new Loan(KnownLoan, "Home", 250000m)));
            }

            Failure failure = request.Id == ForbiddenLoan
                ? new UnauthorizedFailure($"You may not read loan {request.Id}.")
                : new NotFoundFailure("Loan", request.Id.ToString());
            return new ValueTask<Result<Loan>>(Result.Failure<Loan>(failure));
        }
    }

    private sealed class DeleteLoanHandler(LoanStore store,
        IContext context) : ICommandHandler<DeleteLoan>
    {
        public ValueTask<Result> HandleAsync(DeleteLoan request,
            CancellationToken cancellationToken = default)
        {
            store.Deleted.Add(request.Id);
            store.TraceIds.Add(context.TraceId);
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly McpServer _server;
        private readonly Task _serverRun;

        private Harness(ServiceProvider services,
            McpServer server,
            Task serverRun,
            McpClient client)
        {
            Services = services;
            _server = server;
            _serverRun = serverRun;
            Client = client;
        }

        public ServiceProvider Services { get; }

        public McpClient Client { get; }

        public static async Task<Harness> StartAsync(bool hideExistence)
        {
            Pipe clientToServer = new(), serverToClient = new();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<LoanStore>();
            services.AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<GetLoanHandler, GetLoan, Loan>();
                cfg.RegisterRequestHandler<DeleteLoanHandler, DeleteLoan>();
            });
            services.AddSynapseMcp(o =>
            {
                o.HideExistence = hideExistence;
                o.JsonContext = TestJsonContext.Default;
            });
            services.AddMcpServer()
                .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
                .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
                {
                    // The documented way to continue the client's flow: read _meta through the carrier and hand
                    // the result to Synapse before the tool runs.
                    var store = context.Services!.GetRequiredService<IInboundContextStore>();
                    var propagator = context.Services!.GetRequiredService<IContextPropagator>();
                    store.Inbound = propagator.Extract(new McpMetaPropagationCarrier(context.Params!));
                    return await next(context, cancellationToken);
                }))
                .WithTools<LoanTools>();

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var server = provider.GetRequiredService<McpServer>();
            var run = server.RunAsync(TestContext.Current.CancellationToken);

            var client = await McpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
                cancellationToken: TestContext.Current.CancellationToken);

            return new Harness(provider, server, run, client);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _server.DisposeAsync();
            try
            {
                await _serverRun;
            }
            catch (OperationCanceledException)
            {
                // The server stops by cancellation once the client is gone.
            }

            await Services.DisposeAsync();
        }
    }
}
