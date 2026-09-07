using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class SelfHandledEndpointTests
{
    [Fact]
    public async Task Invoke_WithRequestThatIsNotAMessage_RunsExecuteAsyncAndWritesItsResponse()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new ProbeQueryBinder("live"));
        EndpointRegistry.RegisterMetadata<ProbeEndpoint>(new EndpointMetadata(["GET"], "/health/{probe}"));

        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new ProbeEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<ProbeEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<ProbeDto>(
            context.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("live", body!.Probe);
    }


    [Fact]
    public async Task Invoke_WhenExecuteAsyncFails_AnswersIdenticallyToTheSameFailureDispatched()
    {
        // Arrange: the same NotFoundFailure, once returned by a self-handled endpoint and once
        // returned by a handler behind Endpoint<TRequest, TResponse>. Both run against the real
        // HttpInvoker and the real DefaultFailureHttpMapper, so any difference is the tier's.
        EndpointRegistry.RegisterBinder(new MissingProbeQueryBinder());
        EndpointRegistry.RegisterBinder(new MissingProbeCommandBinder());
        EndpointRegistry.RegisterMetadata<MissingProbeEndpoint>(new EndpointMetadata(["GET"], "/missing"));
        EndpointRegistry.RegisterMetadata<DispatchedMissingProbeEndpoint>(
            new EndpointMetadata(["GET"], "/missing-dispatched"));

        var mediator = Substitute.For<IInvoker>();
        mediator.InvokeAsync(Arg.Any<MissingProbeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure<ProbeDto>(Missing())));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        // Act
        var selfHandled = await InvokeAsync(new MissingProbeEndpoint(),
            EndpointRegistry.GetMetadata<MissingProbeEndpoint>(), provider);
        var dispatched = await InvokeAsync(new DispatchedMissingProbeEndpoint(),
            EndpointRegistry.GetMetadata<DispatchedMissingProbeEndpoint>(), provider);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, selfHandled.StatusCode);
        Assert.Equal(dispatched.StatusCode, selfHandled.StatusCode);
        Assert.Equal(dispatched.ContentType, selfHandled.ContentType);
        Assert.Equal(dispatched.Body, selfHandled.Body);
    }


    [Fact]
    public async Task Invoke_WithConfiguredCreatedMapping_UsesItInsteadOfOnSuccess()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new CreatedProbeQueryBinder());
        EndpointRegistry.RegisterMetadata<CreatedProbeEndpoint>(new EndpointMetadata(["POST"], "/probes"));

        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();

        // Act
        var response = await InvokeAsync(new CreatedProbeEndpoint(),
            EndpointRegistry.GetMetadata<CreatedProbeEndpoint>(), services.BuildServiceProvider());

        // Assert
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WithOverriddenOnSuccess_WritesTheResultThatOverrideReturns()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new AcceptedProbeQueryBinder());
        EndpointRegistry.RegisterMetadata<AcceptedProbeEndpoint>(new EndpointMetadata(["POST"], "/probes-accepted"));

        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();

        // Act
        var response = await InvokeAsync(new AcceptedProbeEndpoint(),
            EndpointRegistry.GetMetadata<AcceptedProbeEndpoint>(), services.BuildServiceProvider());

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, response.StatusCode);
    }


    [Fact]
    public async Task Invoke_WhenBindingFails_Answers400WithoutRunningExecuteAsync()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new RejectingProbeQueryBinder());
        EndpointRegistry.RegisterMetadata<RejectingProbeEndpoint>(new EndpointMetadata(["GET"], "/probes-rejected"));

        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var endpoint = new RejectingProbeEndpoint();

        // Act
        var response = await InvokeAsync(endpoint,
            EndpointRegistry.GetMetadata<RejectingProbeEndpoint>(), services.BuildServiceProvider());

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.False(endpoint.Ran);
    }

    [Fact]
    public async Task Invoke_WhenOnBeforeHandleAsyncShortCircuits_DoesNotRunExecuteAsync()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new ShortCircuitProbeQueryBinder());
        EndpointRegistry.RegisterMetadata<ShortCircuitProbeEndpoint>(
            new EndpointMetadata(["GET"], "/probes-short-circuit"));

        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var endpoint = new ShortCircuitProbeEndpoint();

        // Act
        var response = await InvokeAsync(endpoint,
            EndpointRegistry.GetMetadata<ShortCircuitProbeEndpoint>(), services.BuildServiceProvider());

        // Assert
        Assert.Equal(StatusCodes.Status304NotModified, response.StatusCode);
        Assert.False(endpoint.Ran);
    }

    private static NotFoundFailure Missing()
    {
        return new NotFoundFailure("Probe", "live");
    }

    private static async Task<(int StatusCode, string? ContentType, string Body)> InvokeAsync(
        EndpointBase endpoint,
        EndpointMetadata metadata,
        IServiceProvider provider)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();

        await endpoint.CreateDescriptor(metadata).InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, context.Response.ContentType, body);
    }

    internal sealed record ProbeQuery(string Probe);

    internal sealed record ProbeDto(string Probe);

    internal sealed partial class ProbeEndpoint : SelfHandledEndpoint<ProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(ProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    private sealed class ProbeQueryBinder : IEndpointBinder<ProbeQuery>
    {
        private readonly string _probe;

        public ProbeQueryBinder(string probe)
        {
            _probe = probe;
        }

        public ValueTask<BindResult<ProbeQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<ProbeQuery>.Success(new ProbeQuery(_probe)));
        }
    }

    internal sealed record MissingProbeQuery(string Probe);

    internal sealed record MissingProbeCommand(string Probe) : IRequest<ProbeDto>;

    internal sealed partial class MissingProbeEndpoint : SelfHandledEndpoint<MissingProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(MissingProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Failure<ProbeDto>(Missing()));
        }
    }

    internal sealed partial class DispatchedMissingProbeEndpoint : Endpoint<MissingProbeCommand, ProbeDto>;

    private sealed class MissingProbeQueryBinder : IEndpointBinder<MissingProbeQuery>
    {
        public ValueTask<BindResult<MissingProbeQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<MissingProbeQuery>.Success(new MissingProbeQuery("live")));
        }
    }

    private sealed class MissingProbeCommandBinder : IEndpointBinder<MissingProbeCommand>
    {
        public ValueTask<BindResult<MissingProbeCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<MissingProbeCommand>.Success(new MissingProbeCommand("live")));
        }
    }

    internal sealed record CreatedProbeQuery(string Probe);

    internal sealed partial class CreatedProbeEndpoint : SelfHandledEndpoint<CreatedProbeQuery, ProbeDto>
    {
        public override void Configure(IEndpointBuilder<ProbeDto> builder)
        {
            builder.Created(response => $"/probes/{response.Probe}");
        }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(CreatedProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    private sealed class CreatedProbeQueryBinder : IEndpointBinder<CreatedProbeQuery>
    {
        public ValueTask<BindResult<CreatedProbeQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<CreatedProbeQuery>.Success(new CreatedProbeQuery("live")));
        }
    }

    internal sealed record AcceptedProbeQuery(string Probe);

    internal sealed partial class AcceptedProbeEndpoint : SelfHandledEndpoint<AcceptedProbeQuery, ProbeDto>
    {
        public override Microsoft.AspNetCore.Http.IResult OnSuccess(ProbeDto response,
            HttpContext context)
        {
            return TypedResults.Accepted($"/probes/{response.Probe}", response);
        }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(AcceptedProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    private sealed class AcceptedProbeQueryBinder : IEndpointBinder<AcceptedProbeQuery>
    {
        public ValueTask<BindResult<AcceptedProbeQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<AcceptedProbeQuery>.Success(new AcceptedProbeQuery("live")));
        }
    }

    internal sealed record RejectingProbeQuery(string Probe);

    internal sealed partial class RejectingProbeEndpoint : SelfHandledEndpoint<RejectingProbeQuery, ProbeDto>
    {
        public bool Ran { get; private set; }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(RejectingProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            Ran = true;
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    private sealed class RejectingProbeQueryBinder : IEndpointBinder<RejectingProbeQuery>
    {
        public ValueTask<BindResult<RejectingProbeQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<RejectingProbeQuery>.Failure("probe", "is required."));
        }
    }

    internal sealed record ShortCircuitProbeQuery(string Probe);

    internal sealed partial class ShortCircuitProbeEndpoint : SelfHandledEndpoint<ShortCircuitProbeQuery, ProbeDto>
    {
        public bool Ran { get; private set; }

        protected override ValueTask<Microsoft.AspNetCore.Http.IResult?> OnBeforeHandleAsync(
            ShortCircuitProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<Microsoft.AspNetCore.Http.IResult?>(
                TypedResults.StatusCode(StatusCodes.Status304NotModified));
        }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(ShortCircuitProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            Ran = true;
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    private sealed class ShortCircuitProbeQueryBinder : IEndpointBinder<ShortCircuitProbeQuery>
    {
        public ValueTask<BindResult<ShortCircuitProbeQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(
                BindResult<ShortCircuitProbeQuery>.Success(new ShortCircuitProbeQuery("live")));
        }
    }
}
