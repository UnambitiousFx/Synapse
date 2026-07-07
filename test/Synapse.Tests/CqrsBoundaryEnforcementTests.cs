using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests;

public sealed class CqrsBoundaryEnforcementTests
{
    // Exercises the public runtime enforcement API. It is the same closed registration the source generator
    // emits per discovered handler for [assembly: EnableSynapseCqrsBoundaryEnforcement], and the supported way
    // to enforce manually-registered handlers the generator cannot see (known-issue 018). Enforcement only
    // fires when every request type involved (outer + nested) carries the behavior.
    private static void EnableCqrs<TRequest>(ISynapseConfig cfg)
        where TRequest : IRequest
    {
        cfg.RegisterCqrsBoundaryEnforcement<TRequest>();
    }

    private static void EnableCqrs<TRequest, TResponse>(ISynapseConfig cfg)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        cfg.RegisterCqrsBoundaryEnforcement<TRequest, TResponse>();
    }

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
            EnableCqrs<FirstRequest>(cfg);
            EnableCqrs<SecondRequest>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken));

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
            EnableCqrs<FirstRequest>(cfg);
            EnableCqrs<FirstRequestWithResponse, int>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken)
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
            EnableCqrs<FirstRequestWithResponse, int>(cfg);
            EnableCqrs<SecondRequestWithResponse, string>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
            await sender.InvokeAsync(new FirstRequestWithResponse(), TestContext.Current.CancellationToken)
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
            // Enforcement intentionally NOT enabled — no CqrsBoundaryEnforcementBehavior registered.
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert - Should not throw
        var result = await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken);
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
            EnableCqrs<FirstRequest>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert - Should not throw
        var result = await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken);
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
            EnableCqrs<FirstRequestWithResponse, int>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert - Should not throw
        var result = await sender.InvokeAsync(new FirstRequestWithResponse(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        if (result.TryGet(out var value, out _))
        {
            Assert.Equal(42, value);
        }
        else
        {
            Assert.Fail("Result should be successful");
        }
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
                EnableCqrs<FirstRequest>(cfg);
                EnableCqrs<SecondRequest>(cfg);
            });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert - Should not throw when requests are independent (not nested)
        var result1 = await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken);
        Assert.True(result1.IsSuccess);

        var result2 = await sender.InvokeAsync(new SecondRequest(), TestContext.Current.CancellationToken);
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
            EnableCqrs<FirstRequest>(cfg);
            EnableCqrs<SecondRequest>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken)
            );

        // Verify error message contains useful debugging information
        Assert.Contains("Cannot send request 'SecondRequest'", exception.Message);
        Assert.Contains("within a request handler", exception.Message);
        Assert.Contains("previously crossed by 'FirstRequest'", exception.Message);
    }

    [Fact]
    public async Task Duplicate_cqrs_registration_is_deduplicated_and_does_not_throw()
    {
        // Arrange — two RegisterGroups both register CQRS enforcement for the same request, simulating a
        // composition root and a referenced library that both opt in. The behavior is not idempotent, so a
        // duplicate registration would throw on every request — dedup must collapse them to one.
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            cfg.AddRegisterGroup(new CqrsForSecondRequestGroup());
            cfg.AddRegisterGroup(new CqrsForSecondRequestGroup());
        });

        // Assert — only one CQRS descriptor survived for the request.
        var cqrsDescriptors = services.Count(d =>
            d.ServiceType == typeof(IRequestPipelineBehavior<SecondRequest>) &&
            d.ImplementationType == typeof(CqrsBoundaryEnforcementBehavior<SecondRequest>));
        Assert.Equal(1, cqrsDescriptors);

        // Act — dispatch a top-level request; a duplicate would have thrown a spurious boundary violation.
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();
        var result = await sender.InvokeAsync(new SecondRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
    }

    private sealed class CqrsForSecondRequestGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterCqrsBoundaryEnforcement<SecondRequest>();
        }
    }

    [Fact]
    public async Task Manual_enforcement_enforces_a_runtime_registered_handler_the_generator_cannot_see()
    {
        // Arrange — known-issue 018: a handler registered manually at runtime is invisible to the generator,
        // so it gets no generated enforcement. The public RegisterCqrsBoundaryEnforcement API closes that gap.
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestHandlerThatSendsSecondRequest, FirstRequest>();
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            cfg.RegisterCqrsBoundaryEnforcement<FirstRequest>();
            cfg.RegisterCqrsBoundaryEnforcement<SecondRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert — the nested send is now detected.
        var exception = await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
            await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("CQRS boundary violation", exception.Message);
        Assert.Contains("SecondRequest", exception.Message);
        Assert.Contains("FirstRequest", exception.Message);
    }

    [Fact]
    public async Task Manual_and_generated_enforcement_for_same_request_is_deduplicated()
    {
        // Arrange — the realistic 018 overlap: the manual runtime API (composition root) and a generator-style
        // RegisterGroup (a referenced library that opted in) both enforce the same request. The behavior is not
        // idempotent, so without dedup a single send would throw a spurious violation.
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            cfg.RegisterCqrsBoundaryEnforcement<SecondRequest>();
            cfg.AddRegisterGroup(new CqrsForSecondRequestGroup());
        });

        // Assert — only one CQRS descriptor survived for the request.
        var cqrsDescriptors = services.Count(d =>
            d.ServiceType == typeof(IRequestPipelineBehavior<SecondRequest>) &&
            d.ImplementationType == typeof(CqrsBoundaryEnforcementBehavior<SecondRequest>));
        Assert.Equal(1, cqrsDescriptors);

        // Act — a top-level send must succeed; a duplicate would have thrown a spurious boundary violation.
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();
        var result = await sender.InvokeAsync(new SecondRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
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
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert - Should not throw because enforcement is not enabled
        var result = await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken);
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
            EnableCqrs<FirstRequest>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
                await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken)
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
            EnableCqrs<FirstRequestWithResponse, int>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CqrsBoundaryViolationException>(async () =>
            await sender.InvokeAsync(new FirstRequestWithResponse(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("CQRS boundary enforcement metadata was missing", exception.Message);
        Assert.Contains("violation of the CQRS boundary enforcement behavior", exception.Message);
    }

    [Fact]
    public async Task Should_clear_boundary_marker_after_handler_throws_so_next_send_in_scope_succeeds()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestHandlerThatThrows, FirstRequest>();
            cfg.RegisterRequestHandler<ValidSecondRequestHandler, SecondRequest>();
            EnableCqrs<FirstRequest>(cfg);
            EnableCqrs<SecondRequest>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert — the original handler exception is surfaced, not masked.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sender.InvokeAsync(new FirstRequest(), TestContext.Current.CancellationToken));
        Assert.Equal("handler boom", exception.Message);

        // A later independent send in the same scope must see a clean boundary state.
        var result = await sender.InvokeAsync(new SecondRequest(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Should_clear_boundary_marker_after_request_with_response_handler_throws()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FirstRequestWithResponseHandlerThatThrows, FirstRequestWithResponse, int>();
            cfg.RegisterRequestHandler<ValidSecondRequestWithResponseHandler, SecondRequestWithResponse, string>();
            EnableCqrs<FirstRequestWithResponse, int>(cfg);
            EnableCqrs<SecondRequestWithResponse, string>(cfg);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act & Assert — the original handler exception is surfaced, not masked.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sender.InvokeAsync(new FirstRequestWithResponse(), TestContext.Current.CancellationToken));
        Assert.Equal("handler boom", exception.Message);

        // A later independent send in the same scope must see a clean boundary state.
        var result = await sender.InvokeAsync(new SecondRequestWithResponse(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }

    // Test request definitions
    private sealed record FirstRequest : IRequest;

    private sealed record SecondRequest : IRequest;

    private sealed record FirstRequestWithResponse : IRequest<int>;

    private sealed record SecondRequestWithResponse : IRequest<string>;

    // Handler that attempts to send another request (violates CQRS boundary)
    private sealed class FirstRequestHandlerThatSendsSecondRequest : IRequestHandler<FirstRequest>
    {
        private readonly IInvoker _invoker;

        public FirstRequestHandlerThatSendsSecondRequest(IInvoker invoker)
        {
            _invoker = invoker;
        }

        public async ValueTask<Result> HandleAsync(FirstRequest request,
            CancellationToken cancellationToken = default)
        {
            // This should throw CqrsBoundaryViolationException
            await _invoker.InvokeAsync(new SecondRequest(), cancellationToken);
            return Result.Success();
        }
    }

    // Handler that attempts to send a request with response (violates CQRS boundary)
    private sealed class FirstRequestHandlerThatSendsRequestWithResponse : IRequestHandler<FirstRequest>
    {
        private readonly IInvoker _invoker;

        public FirstRequestHandlerThatSendsRequestWithResponse(IInvoker invoker)
        {
            _invoker = invoker;
        }

        public async ValueTask<Result> HandleAsync(FirstRequest request,
            CancellationToken cancellationToken = default)
        {
            // This should throw CqrsBoundaryViolationException
            await _invoker.InvokeAsync(new FirstRequestWithResponse(), cancellationToken);
            return Result.Success();
        }
    }

    // Handler with response that attempts to send another request (violates CQRS boundary)
    private sealed class
        FirstRequestWithResponseHandlerThatSendsRequest : IRequestHandler<FirstRequestWithResponse, int>
    {
        private readonly IInvoker _invoker;

        public FirstRequestWithResponseHandlerThatSendsRequest(IInvoker invoker)
        {
            _invoker = invoker;
        }

        public async ValueTask<Result<int>> HandleAsync(FirstRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            // This should throw CqrsBoundaryViolationException
            await _invoker.InvokeAsync(new SecondRequestWithResponse(), cancellationToken);
            return Result.Success(42);
        }
    }

    // Handler that throws a normal (non-boundary) exception
    private sealed class FirstRequestHandlerThatThrows : IRequestHandler<FirstRequest>
    {
        public ValueTask<Result> HandleAsync(FirstRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("handler boom");
        }
    }

    // Handler with response that throws a normal (non-boundary) exception
    private sealed class FirstRequestWithResponseHandlerThatThrows : IRequestHandler<FirstRequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(FirstRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("handler boom");
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