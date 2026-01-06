using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Tests;

public sealed class CqrsBoundaryEnforcementTests
{
    [Fact]
    public async Task Should_throw_when_request_sends_another_request_within_handler_when_enforcement_enabled()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestHandlerThatSendsSecondRequest, FirstRequest>();
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.SendAsync(new FirstRequest()));

        Assert.Contains("CQRS boundary violation", exception.Message);
        Assert.Contains("SecondRequest", exception.Message);
        Assert.Contains("FirstRequest", exception.Message);
    }

    [Fact]
    public async Task Should_throw_when_request_sends_request_with_response_within_handler_when_enforcement_enabled()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestHandlerThatSendsRequestWithResponse, FirstRequest>();
            cfg.RegisterRequestHandler<ValidFirstRequestWithResponseHandler, FirstRequestWithResponse, int>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.SendAsync(new FirstRequest())
            );

        Assert.Contains("CQRS boundary violation", exception.Message);
        Assert.Contains("FirstRequestWithResponse", exception.Message);
        Assert.Contains("FirstRequest", exception.Message);
    }

    [Fact]
    public async Task
        Should_throw_when_request_with_response_sends_another_request_within_handler_when_enforcement_enabled()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestWithResponseHandlerThatSendsRequest, FirstRequestWithResponse,
                int>();
            cfg.RegisterRequestHandler<ValidSecondRequestWithResponseHandler, SecondRequestWithResponse, string>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
            await sender.SendAsync<FirstRequestWithResponse, int>(new FirstRequestWithResponse())
        );

        Assert.Contains("CQRS boundary violation", exception.Message);
        Assert.Contains("SecondRequestWithResponse", exception.Message);
        Assert.Contains("FirstRequestWithResponse", exception.Message);
    }

    [Fact]
    public async Task Should_not_throw_when_request_sends_another_request_within_handler_when_enforcement_disabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestHandlerThatSendsSecondRequest, FirstRequest>();
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            cfg.EnableCqrsBoundaryEnforcement(false); // Explicitly disabled
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert - Should not throw
        var result = await sender.SendAsync(new FirstRequest());
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Should_not_throw_when_no_nested_requests_when_enforcement_enabled()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<ValidFirstRequestHandler, FirstRequest>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert - Should not throw
        var result = await sender.SendAsync(new FirstRequest());
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Should_not_throw_when_request_with_response_has_no_nested_requests_when_enforcement_enabled()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<ValidFirstRequestWithResponseHandler, FirstRequestWithResponse, int>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert - Should not throw
        var result = await sender.SendAsync<FirstRequestWithResponse, int>(new FirstRequestWithResponse());
        Assert.True(result.IsSuccess);
        if (result.TryGet(out int value))
            Assert.Equal(42, value);
        else
            Assert.Fail("Result should be successful");
    }

    [Fact]
    public async Task Should_allow_sequential_independent_requests_when_enforcement_enabled()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<ValidFirstRequestHandler, FirstRequest>();
                cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
                cfg.EnableCqrsBoundaryEnforcement();
            });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert - Should not throw when requests are independent (not nested)
        var result1 = await sender.SendAsync(new FirstRequest());
        Assert.True(result1.IsSuccess);

        var result2 = await sender.SendAsync(new SecondRequest());
        Assert.True(result2.IsSuccess);
    }

    [Fact]
    public async Task Should_provide_clear_error_message_with_request_names()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestHandlerThatSendsSecondRequest, FirstRequest>();
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.SendAsync(new FirstRequest())
            );

        // Verify error message contains useful debugging information
        Assert.Contains("Cannot send request 'SecondRequest'", exception.Message);
        Assert.Contains("within a request handler", exception.Message);
        Assert.Contains("previously crossed by 'FirstRequest'", exception.Message);
    }

    [Fact]
    public async Task EnableCqrsBoundaryEnforcement_should_be_disabled_by_default()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestHandlerThatSendsSecondRequest, FirstRequest>();
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            // Not calling EnableCqrsBoundaryEnforcement at all - should be disabled by default
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert - Should not throw because enforcement is not enabled
        var result = await sender.SendAsync(new FirstRequest());
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Should_throw_when_user_manually_removes_boundary_enforcement_key_from_context()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<RequestHandlerThatRemovesBoundaryKey, FirstRequest>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.SendAsync(new FirstRequest())
            );

        Assert.Contains("CQRS boundary enforcement metadata was missing", exception.Message);
        Assert.Contains("violation of the CQRS boundary enforcement behavior", exception.Message);
    }

    [Fact]
    public async Task
        Should_throw_when_user_manually_removes_boundary_enforcement_key_from_context_in_request_with_response()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<RequestWithResponseHandlerThatRemovesBoundaryKey, FirstRequestWithResponse,
                int>();
            cfg.EnableCqrsBoundaryEnforcement();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
            await sender.SendAsync<FirstRequestWithResponse, int>(new FirstRequestWithResponse())
        );

        Assert.Contains("CQRS boundary enforcement metadata was missing", exception.Message);
        Assert.Contains("violation of the CQRS boundary enforcement behavior", exception.Message);
    }

    // Test request definitions
    private sealed record FirstRequest : IRequest;

    private sealed record SecondRequest : IRequest;

    private sealed record FirstRequestWithResponse : IRequest<int>;

    private sealed record SecondRequestWithResponse : IRequest<string>;

    // Handler that attempts to send another request (violates CQRS boundary)
    private sealed class FirstRequestHandlerThatSendsSecondRequest : IRequestHandler<FirstRequest>
    {
        private readonly ISender _sender;

        public FirstRequestHandlerThatSendsSecondRequest(ISender sender)
        {
            _sender = sender;
        }

        public async ValueTask<Result> HandleAsync(FirstRequest request,
            CancellationToken cancellationToken = default)
        {
            // This should throw CqrsBoundaryViolationException
            await _sender.SendAsync(new SecondRequest(), cancellationToken);
            return Result.Success();
        }
    }

    // Handler that attempts to send a request with response (violates CQRS boundary)
    private sealed class FirstRequestHandlerThatSendsRequestWithResponse : IRequestHandler<FirstRequest>
    {
        private readonly ISender _sender;

        public FirstRequestHandlerThatSendsRequestWithResponse(ISender sender)
        {
            _sender = sender;
        }

        public async ValueTask<Result> HandleAsync(FirstRequest request,
            CancellationToken cancellationToken = default)
        {
            // This should throw CqrsBoundaryViolationException
            await _sender.SendAsync<FirstRequestWithResponse, int>(new FirstRequestWithResponse(), cancellationToken);
            return Result.Success();
        }
    }

    // Handler with response that attempts to send another request (violates CQRS boundary)
    private sealed class
        FirstRequestWithResponseHandlerThatSendsRequest : IRequestHandler<FirstRequestWithResponse, int>
    {
        private readonly ISender _sender;

        public FirstRequestWithResponseHandlerThatSendsRequest(ISender sender)
        {
            _sender = sender;
        }

        public async ValueTask<Result<int>> HandleAsync(FirstRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            // This should throw CqrsBoundaryViolationException
            await _sender.SendAsync<SecondRequestWithResponse, string>(new SecondRequestWithResponse(),
                cancellationToken);
            return Result.Success(42);
        }
    }

    // Handler that attempts to manually remove the boundary enforcement key
    private sealed class RequestHandlerThatRemovesBoundaryKey : IRequestHandler<FirstRequest>
    {
        private readonly IContext _context;

        public RequestHandlerThatRemovesBoundaryKey(IContext context)
        {
            _context = context;
        }

        public ValueTask<Result> HandleAsync(FirstRequest request,
            CancellationToken cancellationToken = default)
        {
            // Malicious attempt to remove the boundary enforcement key
            _context.RemoveMetadata("__CQRSBoundaryEnforcement");
            return new ValueTask<Result>(Result.Success());
        }
    }

    // Handler with response that attempts to manually remove the boundary enforcement key
    private sealed class
        RequestWithResponseHandlerThatRemovesBoundaryKey : IRequestHandler<FirstRequestWithResponse, int>
    {
        private readonly IContext _context;

        public RequestWithResponseHandlerThatRemovesBoundaryKey(IContext context)
        {
            _context = context;
        }

        public ValueTask<Result<int>> HandleAsync(FirstRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            // Malicious attempt to remove the boundary enforcement key
            _context.RemoveMetadata("__CQRSBoundaryEnforcement");
            return new ValueTask<Result<int>>(Result.Success(42));
        }
    }

    // Valid handlers that don't violate boundaries
    private sealed class ValidSecondRequestHandler : IRequestHandler<SecondRequest>
    {
        public ValueTask<Result> HandleAsync(SecondRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class ValidFirstRequestHandler : IRequestHandler<FirstRequest>
    {
        public ValueTask<Result> HandleAsync(FirstRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class ValidFirstRequestWithResponseHandler : IRequestHandler<FirstRequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(FirstRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(42));
        }
    }

    private sealed class ValidSecondRequestWithResponseHandler : IRequestHandler<SecondRequestWithResponse, string>
    {
        public ValueTask<Result<string>> HandleAsync(SecondRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<string>>(Result.Success("test"));
        }
    }
}