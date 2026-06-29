using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Generator;

namespace UnambitiousFx.Synapse.Generator.Tests;

/// <summary>
/// Tests the source generator's handling of [PipelineBehavior]-annotated classes.
/// Each test compiles minimal C# source, runs SynapseGenerator against it, and
/// asserts on the content of the generated RegisterGroup.g.cs.
/// </summary>
public sealed class GeneratorBehaviorTests
{
    // ── Open-generic no-response request behavior ──────────────────────────

    [Fact]
    public void OpenGenericNoResponseBehavior_CrossProductsWithMatchingHandler()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class LoggingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.LoggingBehavior<global::TestNs.MyRequest>, global::TestNs.MyRequest>()",
            generated);
    }

    // ── Open-generic with-response request behavior ───────────────────────

    [Fact]
    public void OpenGenericWithResponseBehavior_CrossProductsWithMatchingHandler()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest<int>;

            [RequestHandler<MyRequest, int>]
            public sealed class MyHandler : IRequestHandler<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(42));
            }

            [PipelineBehavior]
            public sealed class LoggingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.LoggingBehavior<global::TestNs.MyRequest, int>, global::TestNs.MyRequest, int>()",
            generated);
    }

    // ── Open-generic event behavior ───────────────────────────────────────

    [Fact]
    public void OpenGenericEventBehavior_CrossProductsWithMatchingEventHandler()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record UserCreated : IEvent;

            [EventHandler<UserCreated>]
            public sealed class UserCreatedHandler : IEventHandler<UserCreated>
            {
                public ValueTask<Result> HandleAsync(UserCreated @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class EventLoggingBehavior<TEvent> : IEventPipelineBehavior<TEvent>
                where TEvent : class, IEvent
            {
                public ValueTask<Result> HandleAsync(TEvent @event, EventHandlerDelegate<TEvent> next, CancellationToken ct = default)
                    => next(@event, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterEventPipelineBehavior<global::TestNs.EventLoggingBehavior<global::TestNs.UserCreated>, global::TestNs.UserCreated>()",
            generated);
    }

    // ── Constrained open-generic event behavior ──────────────────────────

    [Fact]
    public void ConstrainedOpenGenericEventBehavior_OnlyCrossProductsWithMatchingEvents()
    {
        // Arrange (Given) — an open-generic event behavior constrained to a marker interface. It must
        // cross-product only with event handlers whose event implements that marker, exactly like the
        // request-behavior constraint filtering.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public interface IAuditableEvent : IEvent;

            public sealed record UserCreated : IEvent;
            public sealed record OrderPlaced : IAuditableEvent;

            [EventHandler<UserCreated>]
            public sealed class UserCreatedHandler : IEventHandler<UserCreated>
            {
                public ValueTask<Result> HandleAsync(UserCreated @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [EventHandler<OrderPlaced>]
            public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced>
            {
                public ValueTask<Result> HandleAsync(OrderPlaced @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class AuditEventBehavior<TEvent> : IEventPipelineBehavior<TEvent>
                where TEvent : class, IAuditableEvent
            {
                public ValueTask<Result> HandleAsync(TEvent @event, EventHandlerDelegate<TEvent> next, CancellationToken ct = default)
                    => next(@event, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — only the auditable event is wrapped; the plain event is not.
        Assert.Contains(
            "builder.RegisterEventPipelineBehavior<global::TestNs.AuditEventBehavior<global::TestNs.OrderPlaced>, global::TestNs.OrderPlaced>()",
            generated);
        Assert.DoesNotContain("AuditEventBehavior<global::TestNs.UserCreated>", generated);
    }

    // ── Open-generic stream behavior ──────────────────────────────────────

    [Fact]
    public void OpenGenericStreamBehavior_CrossProductsWithMatchingStreamHandler()
    {
        // Arrange (Given)
        const string source = """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record DataStream : IStreamRequest<string>;

            [StreamRequestHandler<DataStream, string>]
            public sealed class DataStreamHandler : IStreamRequestHandler<DataStream, string>
            {
                public async IAsyncEnumerable<Result<string>> HandleAsync(DataStream request, [EnumeratorCancellation] CancellationToken ct = default)
                {
                    yield return Result.Success("a");
                    await Task.CompletedTask;
                }
            }

            [PipelineBehavior]
            public sealed class StreamLoggingBehavior<TRequest, TItem> : IStreamRequestPipelineBehavior<TRequest, TItem>
                where TRequest : IStreamRequest<TItem>
                where TItem : notnull
            {
                public async IAsyncEnumerable<Result<TItem>> HandleAsync(TRequest request, StreamRequestHandlerDelegate<TItem> next, [EnumeratorCancellation] CancellationToken ct = default)
                {
                    await foreach (var item in next()) yield return item;
                }
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterStreamRequestPipelineBehavior<global::TestNs.StreamLoggingBehavior<global::TestNs.DataStream, string>, global::TestNs.DataStream, string>()",
            generated);
    }

    // ── Closed (concrete-type) behavior ───────────────────────────────────

    [Fact]
    public void ClosedBehavior_EmitsSingleRegistration()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record SpecialRequest : IRequest;

            [RequestHandler<SpecialRequest>]
            public sealed class SpecialHandler : IRequestHandler<SpecialRequest>
            {
                public ValueTask<Result> HandleAsync(SpecialRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class SpecificBehavior : IRequestPipelineBehavior<SpecialRequest>
            {
                public ValueTask<Result> HandleAsync(SpecialRequest request, RequestHandlerDelegate<SpecialRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — one explicit closed registration, no cross-product
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.SpecificBehavior, global::TestNs.SpecialRequest>()",
            generated);
    }

    // ── Emission order is deterministic (namespace + class name) ──────────
    // Runtime pipeline position is decided by IOrderedPipelineBehavior, not by emission order.
    // The generator only needs a stable, reproducible registration sequence, keyed by
    // namespace then class name.

    [Fact]
    public void BehaviorEmission_OrderedByClassNameDeterministically()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class ZuluBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }

            [PipelineBehavior]
            public sealed class AlphaBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — AlphaBehavior must be emitted before ZuluBehavior (class-name ordinal sort),
        // regardless of declaration order in the source.
        var alphaPos = generated.IndexOf("AlphaBehavior", StringComparison.Ordinal);
        var zuluPos = generated.IndexOf("ZuluBehavior", StringComparison.Ordinal);
        Assert.True(alphaPos >= 0, "AlphaBehavior registration not found");
        Assert.True(zuluPos >= 0, "ZuluBehavior registration not found");
        Assert.True(alphaPos < zuluPos, "AlphaBehavior should be emitted before ZuluBehavior");
    }

    // ── Multiple handlers — open-generic behavior cross-products with all ─

    [Fact]
    public void OpenGenericBehavior_CrossProductsWithAllMatchingHandlers()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record RequestA : IRequest;
            public sealed record RequestB : IRequest;

            [RequestHandler<RequestA>]
            public sealed class HandlerA : IRequestHandler<RequestA>
            {
                public ValueTask<Result> HandleAsync(RequestA request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [RequestHandler<RequestB>]
            public sealed class HandlerB : IRequestHandler<RequestB>
            {
                public ValueTask<Result> HandleAsync(RequestB request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class GlobalBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — one registration per handler
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.GlobalBehavior<global::TestNs.RequestA>, global::TestNs.RequestA>()",
            generated);
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.GlobalBehavior<global::TestNs.RequestB>, global::TestNs.RequestB>()",
            generated);
    }

    // ── MDG008: [PipelineBehavior] without a pipeline interface ──────────

    [Fact]
    public void BehaviorWithNoInterface_EmitsMDG008Diagnostic()
    {
        // Arrange (Given)
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            [PipelineBehavior]
            public sealed class NotABehavior { }
            """;

        // Act (When)
        var (diagnostics, _) = RunGenerator(source);

        // Assert (Then)
        Assert.Contains(diagnostics, d => d.Id == "MDG008");
    }

    // ── Open-generic behavior does NOT cross-product with wrong handler kind

    [Fact]
    public void OpenGenericNoResponseBehavior_DoesNotCrossProductWithResponseHandler()
    {
        // Arrange (Given) — behavior is IRequestPipelineBehavior<TRequest> (no-response),
        // but handler is IRequestHandler<MyRequest, int> (with-response)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest<int>;

            [RequestHandler<MyRequest, int>]
            public sealed class MyHandler : IRequestHandler<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(0));
            }

            [PipelineBehavior]
            public sealed class NoResponseBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — no cross-product because kinds don't match
        Assert.DoesNotContain("NoResponseBehavior", generated);
    }

    // ── Closed (concrete-type) with-response request behavior ────────────

    [Fact]
    public void ClosedWithResponseBehavior_EmitsSingleRegistration()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest<int>;

            [RequestHandler<MyRequest, int>]
            public sealed class MyHandler : IRequestHandler<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(42));
            }

            [PipelineBehavior]
            public sealed class SpecificWithResponseBehavior : IRequestPipelineBehavior<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, RequestHandlerDelegate<MyRequest, int> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — one explicit closed registration, no cross-product
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.SpecificWithResponseBehavior, global::TestNs.MyRequest, int>()",
            generated);
    }

    // ── Closed (concrete-type) event behavior ─────────────────────────────

    [Fact]
    public void ClosedEventBehavior_EmitsSingleRegistration()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record UserCreated : IEvent;

            [EventHandler<UserCreated>]
            public sealed class UserCreatedHandler : IEventHandler<UserCreated>
            {
                public ValueTask<Result> HandleAsync(UserCreated @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class SpecificEventBehavior : IEventPipelineBehavior<UserCreated>
            {
                public ValueTask<Result> HandleAsync(UserCreated @event, EventHandlerDelegate<UserCreated> next, CancellationToken ct = default)
                    => next(@event, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterEventPipelineBehavior<global::TestNs.SpecificEventBehavior, global::TestNs.UserCreated>()",
            generated);
    }

    // ── Closed (concrete-type) stream behavior ────────────────────────────

    [Fact]
    public void ClosedStreamBehavior_EmitsSingleRegistration()
    {
        // Arrange (Given)
        const string source = """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record DataStream : IStreamRequest<string>;

            [StreamRequestHandler<DataStream, string>]
            public sealed class DataStreamHandler : IStreamRequestHandler<DataStream, string>
            {
                public async IAsyncEnumerable<Result<string>> HandleAsync(DataStream request, [EnumeratorCancellation] CancellationToken ct = default)
                {
                    yield return Result.Success("a");
                    await Task.CompletedTask;
                }
            }

            [PipelineBehavior]
            public sealed class SpecificStreamBehavior : IStreamRequestPipelineBehavior<DataStream, string>
            {
                public async IAsyncEnumerable<Result<string>> HandleAsync(DataStream request, StreamRequestHandlerDelegate<string> next, [EnumeratorCancellation] CancellationToken ct = default)
                {
                    await foreach (var item in next()) yield return item;
                }
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterStreamRequestPipelineBehavior<global::TestNs.SpecificStreamBehavior, global::TestNs.DataStream, string>()",
            generated);
    }

    // ── MDG002: no handler types found in assembly ────────────────────────

    [Fact]
    public void NoHandlers_EmitsMDG002Diagnostic()
    {
        // Arrange (Given) — a class that is not a handler (no handler attribute)
        const string source = """
            namespace TestNs;

            public sealed class SomeService { }
            """;

        // Act (When)
        var (diagnostics, _) = RunGenerator(source);

        // Assert (Then)
        Assert.Contains(diagnostics, d => d.Id == "MDG002");
    }

    // ── MDG010: open-generic behavior with an uninferable extra type parameter ──

    [Fact]
    public void OpenGenericBehavior_WithExtraUnbindableTypeParameter_EmitsMDG010AndNoRegistration()
    {
        // Arrange (Given) — the class declares two type parameters but the interface binds only one,
        // so TState cannot be inferred when the behavior is closed over a handler.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class LogBehavior<TRequest, TState> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var (diagnostics, generated) = RunGenerator(source);

        // Assert (Then) — diagnostic reported and no malformed registration emitted.
        Assert.Contains(diagnostics, d => d.Id == "MDG010");
        Assert.DoesNotContain("LogBehavior<", generated ?? string.Empty);
    }

    // ── Open-generic behavior whose type parameters are declared in a different order than the interface ──

    [Fact]
    public void OpenGenericBehavior_WithReorderedTypeParameters_ClosesInClassOrder()
    {
        // Arrange (Given) — class declares <TResponse, TRequest> but the interface is <TRequest, TResponse>.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest<int>;

            [RequestHandler<MyRequest, int>]
            public sealed class MyHandler : IRequestHandler<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(42));
            }

            [PipelineBehavior]
            public sealed class LoggingBehavior<TResponse, TRequest> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — the behavior is closed in class-declaration order (<TResponse, TRequest> => <int, MyRequest>).
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.LoggingBehavior<int, global::TestNs.MyRequest>, global::TestNs.MyRequest, int>()",
            generated);
    }

    // ── RegisterGroup.g.cs contains RegisterDispatchers for IEvent types ───

    [Fact]
    public void RegisterGroup_ContainsRegisterDispatchers_ForEventTypes()
    {
        // Arrange (Given) — source that contains an IEvent implementation and its handler
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record UserCreated : IEvent;

            [EventHandler<UserCreated>]
            public sealed class UserCreatedHandler : IEventHandler<UserCreated>
            {
                public ValueTask<Result> HandleAsync(UserCreated @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — RegisterGroup now implements IEventDispatcherRegistration; dispatcher
        // registration is folded in (no separate EventDispatcherRegistration.g.cs is emitted).
        Assert.NotNull(generated);
        Assert.Contains("IEventDispatcherRegistration", generated);
        Assert.Contains("RegisterDispatchers", generated);
        Assert.Contains("UserCreated", generated);
        Assert.Contains("DynamicDependency", generated);
    }

    // ── CQRS boundary enforcement via [assembly: EnableSynapseCqrsBoundaryEnforcement] ─

    [Fact]
    public void CqrsEnforcement_WhenAssemblyOptIn_EmitsClosedRegistrationForValueTypeResponse()
    {
        // Arrange (Given) — opt-in attribute + a handler whose response is a value type (int).
        // This is the known-issue 001 regression case: an open-generic descriptor closed over a
        // value type throws under Native AOT, so the generator must emit a CLOSED registration.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: EnableSynapseCqrsBoundaryEnforcement]

            namespace TestNs;

            public sealed record MyRequest : IRequest<int>;

            [RequestHandler<MyRequest, int>]
            public sealed class MyHandler : IRequestHandler<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(42));
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — closed, value-type registration of the built-in enforcement behavior (now emitted via
        // the unified global-behavior path); no open-generic descriptor.
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::UnambitiousFx.Synapse.Pipelines.CqrsBoundaryEnforcementBehavior<global::TestNs.MyRequest, int>, global::TestNs.MyRequest, int>()",
            generated);
        Assert.DoesNotContain("IRequestPipelineBehavior<,>", generated);
    }

    [Fact]
    public void RequestHandler_WithTupleResponse_EmitsCorrectlyGlobalizedRegistration()
    {
        // Arrange (Given) — a query whose response is a value tuple, plus an open-generic behavior so a
        // closed registration is emitted. This is the known-issue 012 regression case: the old string
        // muncher prefixed the whole tuple with `global::`, emitting uncompilable `global::(int, string)`.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record GetTotals : IRequest<(int Open, int Done)>;

            [RequestHandler<GetTotals, (int Open, int Done)>]
            public sealed class GetTotalsHandler : IRequestHandler<GetTotals, (int Open, int Done)>
            {
                public ValueTask<Result<(int Open, int Done)>> HandleAsync(GetTotals request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success((1, 2)));
            }

            [PipelineBehavior]
            public sealed class LoggingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — the tuple flows through verbatim, never gets a malformed `global::(` prefix.
        Assert.DoesNotContain("global::(", generated);
        Assert.Contains(
            "builder.RegisterRequestHandler<global::TestNs.GetTotalsHandler, global::TestNs.GetTotals, (int Open, int Done)>()",
            generated);
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.LoggingBehavior<global::TestNs.GetTotals, (int Open, int Done)>, global::TestNs.GetTotals, (int Open, int Done)>()",
            generated);
    }

    [Fact]
    public void RequestHandler_WithTupleAsGenericArgument_EmitsCorrectlyGlobalizedRegistration()
    {
        // Arrange (Given) — a request type that is itself generic over a value tuple, plus an
        // open-generic behavior so a closed registration is emitted. This is the known-issue 017
        // regression case: the old `SplitTopLevelArgs` counted only `<`/`>`, so the tuple's internal
        // comma was treated as a top-level argument separator and the request type was emitted as
        // uncompilable `global::TestNs.Query<global::(int Id, global:: string Name)>`.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record Query<T>(T Value) : IRequest<int>;

            [RequestHandler<Query<(int Id, string Name)>, int>]
            public sealed class QueryHandler : IRequestHandler<Query<(int Id, string Name)>, int>
            {
                public ValueTask<Result<int>> HandleAsync(Query<(int Id, string Name)> request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(0));
            }

            [PipelineBehavior]
            public sealed class LoggingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — the nested tuple flows through verbatim, never gets a malformed `global::(`.
        Assert.DoesNotContain("global::(", generated);
        Assert.Contains(
            "builder.RegisterRequestHandler<global::TestNs.QueryHandler, global::TestNs.Query<(int Id, string Name)>, int>()",
            generated);
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.LoggingBehavior<global::TestNs.Query<(int Id, string Name)>, int>, global::TestNs.Query<(int Id, string Name)>, int>()",
            generated);
    }

    [Fact]
    public void CqrsEnforcement_WithoutAssemblyOptIn_EmitsNoRegistration()
    {
        // Arrange (Given) — same handler, but no opt-in attribute.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest<int>;

            [RequestHandler<MyRequest, int>]
            public sealed class MyHandler : IRequestHandler<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(42));
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — no CQRS behavior emitted when not opted-in.
        Assert.DoesNotContain("RegisterCqrsBoundaryEnforcement", generated);
    }

    // ── Cross-assembly behavior application ───────────────────────────────

    [Fact]
    public void OpenGenericBehavior_CrossProductsWithHandlerInReferencedAssembly()
    {
        // Arrange (Given) — handler lives in a referenced assembly; the open-generic behavior is
        // declared in the assembly being generated. The behavior must blanket the referenced handler.
        const string referencedSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace LibNs;

            public sealed record LibRequest : IRequest<int>;

            [RequestHandler<LibRequest, int>]
            public sealed class LibHandler : IRequestHandler<LibRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(LibRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        const string mainSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace AppNs;

            [PipelineBehavior]
            public sealed class MetricsBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorWithReference(referencedSource, mainSource);

        // Assert (Then) — closed (AOT-safe) registration over the referenced value-type request.
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::AppNs.MetricsBehavior<global::LibNs.LibRequest, int>, global::LibNs.LibRequest, int>()",
            generated);
    }

    [Fact]
    public void OpenGenericBehavior_WithCrossAssemblyDisabled_DoesNotCrossProductReferencedHandler()
    {
        // Arrange (Given) — same setup, but the main assembly opts out of cross-assembly propagation.
        const string referencedSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace LibNs;

            public sealed record LibRequest : IRequest<int>;

            [RequestHandler<LibRequest, int>]
            public sealed class LibHandler : IRequestHandler<LibRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(LibRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        const string mainSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: DisableSynapseCrossAssemblyBehaviors]

            namespace AppNs;

            [PipelineBehavior]
            public sealed class MetricsBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorWithReference(referencedSource, mainSource);

        // Assert (Then) — the referenced handler is not blanketed when opted out.
        Assert.DoesNotContain("LibRequest", generated);
    }

    [Fact]
    public void CqrsEnforcement_WhenRootOptsIn_EmitsRegistrationForReferencedAssemblyHandler()
    {
        // Arrange (Given) — only the composition root opts into CQRS enforcement; the handler lives in a
        // referenced assembly that does NOT carry the attribute. The root must still cover it.
        const string referencedSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace LibNs;

            public sealed record LibRequest : IRequest<int>;

            [RequestHandler<LibRequest, int>]
            public sealed class LibHandler : IRequestHandler<LibRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(LibRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        const string mainSource = """
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: EnableSynapseCqrsBoundaryEnforcement]

            namespace AppNs;
            """;

        // Act (When)
        var generated = RunGeneratorWithReference(referencedSource, mainSource);

        // Assert (Then) — CQRS enforcement emitted for the referenced value-type request, AOT-safe (via the
        // unified global-behavior path).
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::UnambitiousFx.Synapse.Pipelines.CqrsBoundaryEnforcementBehavior<global::LibNs.LibRequest, int>, global::LibNs.LibRequest, int>()",
            generated);
    }

    // ── Global behaviors via [assembly: SynapseGlobalBehavior(typeof(...))] ─

    [Fact]
    public void GlobalBehavior_WhenOpenGenericRegistered_EmitsClosedRegistrationPerHandler()
    {
        // Arrange (Given) — an open-generic behavior registered globally via the assembly attribute, NOT
        // decorated with [PipelineBehavior]. Two request handlers should each get a closed registration.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestNs.LoggingBehavior<,>))]

            namespace TestNs;

            public sealed record RequestA : IRequest<int>;
            public sealed record RequestB : IRequest<string>;

            [RequestHandler<RequestA, int>]
            public sealed class HandlerA : IRequestHandler<RequestA, int>
            {
                public ValueTask<Result<int>> HandleAsync(RequestA request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            [RequestHandler<RequestB, string>]
            public sealed class HandlerB : IRequestHandler<RequestB, string>
            {
                public ValueTask<Result<string>> HandleAsync(RequestB request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success("x"));
            }

            public sealed class LoggingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — one closed registration per handler.
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.LoggingBehavior<global::TestNs.RequestA, int>, global::TestNs.RequestA, int>()",
            generated);
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.LoggingBehavior<global::TestNs.RequestB, string>, global::TestNs.RequestB, string>()",
            generated);
        AssertGeneratedCompiles(source);
    }

    [Fact]
    public void GlobalBehavior_FromReferencedAssembly_EmitsRegistration()
    {
        // Arrange (Given) — the NuGet scenario: an open-generic behavior defined in a referenced assembly
        // (undecorated), opted into globally at the composition root. The handler also lives in the package.
        const string referencedSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace LibNs;

            public sealed record LibRequest : IRequest<int>;

            [RequestHandler<LibRequest, int>]
            public sealed class LibHandler : IRequestHandler<LibRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(LibRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            public sealed class LoggingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        const string mainSource = """
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(LibNs.LoggingBehavior<,>))]

            namespace AppNs;
            """;

        // Act (When)
        var generated = RunGeneratorWithReference(referencedSource, mainSource);

        // Assert (Then) — closed registration emitted for the referenced value-type request, AOT-safe.
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::LibNs.LoggingBehavior<global::LibNs.LibRequest, int>, global::LibNs.LibRequest, int>()",
            generated);
    }

    [Fact]
    public void GlobalBehavior_WhenTypeImplementsNoPipelineInterface_ReportsDiagnostic()
    {
        // Arrange (Given) — the typeof argument is not a pipeline behavior.
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestNs.NotABehavior))]

            namespace TestNs;

            public sealed class NotABehavior;
            """;

        // Act (When)
        var (diagnostics, _) = RunGenerator(source);

        // Assert (Then)
        Assert.Contains(diagnostics, d => d.Id == "MDG013");
    }

    [Fact]
    public void GlobalBehavior_WhenTypeNotPublic_ReportsDiagnostic()
    {
        // Arrange (Given) — a valid behavior, but non-public, so the generated registration cannot name it.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestNs.SecretBehavior<,>))]

            namespace TestNs;

            internal sealed class SecretBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var (diagnostics, _) = RunGenerator(source);

        // Assert (Then)
        Assert.Contains(diagnostics, d => d.Id == "MDG014");
    }

    // ── [Validator] discovery ─────────────────────────────────────────────

    [Fact]
    public void ValidatorAttribute_WithResponseRequest_EmitsRegisterValidatorWithResponse()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest<int>;

            [RequestHandler<MyRequest, int>]
            public sealed class MyHandler : IRequestHandler<MyRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            [Validator]
            public sealed class MyValidator : IRequestValidator<MyRequest>
            {
                public ValueTask<Result> ValidateAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — response type derived from MyRequest : IRequest<int>.
        Assert.Contains(
            "builder.RegisterValidator<global::TestNs.MyValidator, global::TestNs.MyRequest, int>()",
            generated);
    }

    [Fact]
    public void ValidatorAttribute_NoResponseRequest_EmitsRegisterValidatorWithoutResponse()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [Validator]
            public sealed class MyValidator : IRequestValidator<MyRequest>
            {
                public ValueTask<Result> ValidateAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — two-argument form, no response.
        Assert.Contains(
            "builder.RegisterValidator<global::TestNs.MyValidator, global::TestNs.MyRequest>()",
            generated);
    }

    [Fact]
    public void ValidatorAttribute_OnNonValidatorClass_EmitsDiagnosticAndNoRegistration()
    {
        // Arrange (Given) — [Validator] on a class that does not implement IRequestValidator<>.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [Validator]
            public sealed class NotAValidator
            {
            }
            """;

        // Act (When)
        var (diagnostics, generated) = RunGenerator(source);

        // Assert (Then)
        Assert.Contains(diagnostics, d => d.Id == "MDG009");
        Assert.DoesNotContain("RegisterValidator", generated ?? string.Empty);
    }

    [Fact]
    public void ValidatorAttribute_RequestWithMultipleIRequest_EmitsMDG011AndNoRegistration()
    {
        // Arrange (Given) — the validated request implements two IRequest<TResponse> with distinct
        // response types, so the response binding is ambiguous.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record Foo;
            public sealed record Bar;

            public sealed record MultiReq : IRequest<Foo>, IRequest<Bar>;

            [Validator]
            public sealed class MultiReqValidator : IRequestValidator<MultiReq>
            {
                public ValueTask<Result> ValidateAsync(MultiReq request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var (diagnostics, generated) = RunGenerator(source);

        // Assert (Then) — ambiguity reported, no (wrong) registration emitted.
        Assert.Contains(diagnostics, d => d.Id == "MDG011");
        Assert.DoesNotContain("RegisterValidator", generated ?? string.Empty);
    }

    // ── Nested type declarations (enclosing-type chain) ───────────────────

    [Fact]
    public void NestedRequestHandler_EmitsEnclosingTypeInRegistration()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record CreateTaskCommand : IRequest;

            public static class Tasks
            {
                [RequestHandler<CreateTaskCommand>]
                public sealed class CreateHandler : IRequestHandler<CreateTaskCommand>
                {
                    public ValueTask<Result> HandleAsync(CreateTaskCommand request, CancellationToken ct = default)
                        => ValueTask.FromResult(Result.Success());
                }
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterRequestHandler<global::TestNs.Tasks.CreateHandler, global::TestNs.CreateTaskCommand>()",
            generated);
    }

    [Fact]
    public void NestedClosedBehavior_EmitsEnclosingTypeInRegistration()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            public static class Outer
            {
                [PipelineBehavior]
                public sealed class SpecificBehavior : IRequestPipelineBehavior<MyRequest>
                {
                    public ValueTask<Result> HandleAsync(MyRequest request, RequestHandlerDelegate<MyRequest> next, CancellationToken ct = default)
                        => next(request, ct);
                }
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.Outer.SpecificBehavior, global::TestNs.MyRequest>()",
            generated);
    }

    [Fact]
    public void NestedOpenGenericBehavior_EmitsEnclosingTypeWithoutStrayTypeParameters()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            public static class Outer
            {
                [PipelineBehavior]
                public sealed class LoggingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                    where TRequest : IRequest
                {
                    public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                        => next(request, ct);
                }
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then): base name carries the enclosing type and is closed with the handler's request,
        // with no stray open-generic type parameters left on the base name.
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.Outer.LoggingBehavior<global::TestNs.MyRequest>, global::TestNs.MyRequest>()",
            generated);
    }

    [Fact]
    public void NestedValidator_EmitsEnclosingTypeInRegistration()
    {
        // Arrange (Given)
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record MyRequest : IRequest;

            public static class Outer
            {
                [Validator]
                public sealed class MyValidator : IRequestValidator<MyRequest>
                {
                    public ValueTask<Result> ValidateAsync(MyRequest request, CancellationToken ct = default)
                        => ValueTask.FromResult(Result.Success());
                }
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterValidator<global::TestNs.Outer.MyValidator, global::TestNs.MyRequest>()",
            generated);
    }

    // ── Special generic constraints (class/struct/unmanaged/new()) ────────

    [Fact]
    public void OpenGenericBehavior_StructResponseConstraint_OnlyRegistersValueTypeResponses()
    {
        // Arrange (Given) — a behavior constrained to `where TResponse : struct`. Before the fix the
        // generator dropped the struct constraint and cross-producted every handler, emitting a closed
        // type that violates the constraint (CS0453). It must now register only the value-type response.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record IntRequest : IRequest<int>;
            public sealed record StringRequest : IRequest<string>;

            [RequestHandler<IntRequest, int>]
            public sealed class IntHandler : IRequestHandler<IntRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(IntRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            [RequestHandler<StringRequest, string>]
            public sealed class StringHandler : IRequestHandler<StringRequest, string>
            {
                public ValueTask<Result<string>> HandleAsync(StringRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success("x"));
            }

            [PipelineBehavior]
            public sealed class StructCache<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : struct
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — only the value-type response is wrapped; the reference-type one is excluded and
        // the generated registration compiles.
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.StructCache<global::TestNs.IntRequest, int>, global::TestNs.IntRequest, int>()",
            generated);
        Assert.DoesNotContain("StructCache<global::TestNs.StringRequest", generated);
        AssertGeneratedCompiles(source);
    }

    [Fact]
    public void OpenGenericBehavior_ClassRequestConstraint_OnlyRegistersReferenceTypes()
    {
        // Arrange (Given) — `where TRequest : class` must exclude a value-type (struct) request.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record ClassRequest : IRequest;
            public readonly record struct StructRequest : IRequest;

            [RequestHandler<ClassRequest>]
            public sealed class ClassHandler : IRequestHandler<ClassRequest>
            {
                public ValueTask<Result> HandleAsync(ClassRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [RequestHandler<StructRequest>]
            public sealed class StructHandler : IRequestHandler<StructRequest>
            {
                public ValueTask<Result> HandleAsync(StructRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class ClassOnly<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : class, IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.ClassOnly<global::TestNs.ClassRequest>, global::TestNs.ClassRequest>()",
            generated);
        Assert.DoesNotContain("ClassOnly<global::TestNs.StructRequest", generated);
        AssertGeneratedCompiles(source);
    }

    [Fact]
    public void OpenGenericBehavior_NewConstraint_OnlyRegistersTypesWithParameterlessCtor()
    {
        // Arrange (Given) — `where TRequest : new()` must exclude a request that has no parameterless ctor
        // (a positional record with a required parameter).
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record HasCtor : IRequest;
            public sealed record NoCtor(int X) : IRequest;

            [RequestHandler<HasCtor>]
            public sealed class HasCtorHandler : IRequestHandler<HasCtor>
            {
                public ValueTask<Result> HandleAsync(HasCtor request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [RequestHandler<NoCtor>]
            public sealed class NoCtorHandler : IRequestHandler<NoCtor>
            {
                public ValueTask<Result> HandleAsync(NoCtor request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class NewOnly<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest, new()
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.NewOnly<global::TestNs.HasCtor>, global::TestNs.HasCtor>()",
            generated);
        Assert.DoesNotContain("NewOnly<global::TestNs.NoCtor", generated);
        AssertGeneratedCompiles(source);
    }

    [Fact]
    public void OpenGenericBehavior_UnmanagedResponseConstraint_OnlyRegistersUnmanagedResponses()
    {
        // Arrange (Given) — `where TResponse : unmanaged` must exclude a managed (reference) response.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public sealed record IntRequest : IRequest<int>;
            public sealed record StringRequest : IRequest<string>;

            [RequestHandler<IntRequest, int>]
            public sealed class IntHandler : IRequestHandler<IntRequest, int>
            {
                public ValueTask<Result<int>> HandleAsync(IntRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            [RequestHandler<StringRequest, string>]
            public sealed class StringHandler : IRequestHandler<StringRequest, string>
            {
                public ValueTask<Result<string>> HandleAsync(StringRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success("x"));
            }

            [PipelineBehavior]
            public sealed class UnmanagedCache<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : unmanaged
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then)
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.UnmanagedCache<global::TestNs.IntRequest, int>, global::TestNs.IntRequest, int>()",
            generated);
        Assert.DoesNotContain("UnmanagedCache<global::TestNs.StringRequest", generated);
        AssertGeneratedCompiles(source);
    }

    [Fact]
    public void OpenGenericBehavior_NamedAndSpecialConstraints_BothEnforced()
    {
        // Arrange (Given) — a behavior with both a marker-interface constraint and a `struct` response
        // constraint. A handler must satisfy *both* to be wrapped.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestNs;

            public interface IAuditable { }

            public sealed record AuditedInt : IRequest<int>, IAuditable;
            public sealed record PlainInt : IRequest<int>;
            public sealed record AuditedString : IRequest<string>, IAuditable;

            [RequestHandler<AuditedInt, int>]
            public sealed class AuditedIntHandler : IRequestHandler<AuditedInt, int>
            {
                public ValueTask<Result<int>> HandleAsync(AuditedInt request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            [RequestHandler<PlainInt, int>]
            public sealed class PlainIntHandler : IRequestHandler<PlainInt, int>
            {
                public ValueTask<Result<int>> HandleAsync(PlainInt request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            [RequestHandler<AuditedString, string>]
            public sealed class AuditedStringHandler : IRequestHandler<AuditedString, string>
            {
                public ValueTask<Result<string>> HandleAsync(AuditedString request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success("x"));
            }

            [PipelineBehavior]
            public sealed class AuditValueCache<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>, IAuditable
                where TResponse : struct
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — only the auditable + value-type-response handler qualifies.
        Assert.Contains(
            "builder.RegisterRequestPipelineBehavior<global::TestNs.AuditValueCache<global::TestNs.AuditedInt, int>, global::TestNs.AuditedInt, int>()",
            generated);
        Assert.DoesNotContain("AuditValueCache<global::TestNs.PlainInt", generated);     // fails named constraint
        Assert.DoesNotContain("AuditValueCache<global::TestNs.AuditedString", generated); // fails struct constraint
        AssertGeneratedCompiles(source);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Runs the generator, feeds its output back into the compilation, and asserts there are no
    ///     compiler errors — the core regression guard: an open-generic behavior closed over a handler that
    ///     violates its constraints would surface as CS0453 (and friends) here.
    /// </summary>
    private static void AssertGeneratedCompiles(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SynapseGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(errors.Length == 0,
            "Generated code should compile, but got: " + string.Join("; ", errors.Select(e => e.ToString())));
    }

    private static string RunGeneratorWithReference(string referencedSource, string mainSource)
    {
        var referencedCompilation = CSharpCompilation.Create(
            "ReferencedAssembly",
            [CSharpSyntaxTree.ParseText(referencedSource)],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var references = GetMetadataReferences().Append(referencedCompilation.ToMetadataReference());

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(mainSource)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create(new SynapseGenerator())
            .RunGenerators(compilation);

        var generatedFile = driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("RegisterGroup.g.cs", StringComparison.Ordinal));

        return generatedFile?.GetText().ToString() ?? string.Empty;
    }

    private static string RunGeneratorAndGetRegistrationGroup(string source)
    {
        var (_, generated) = RunGenerator(source);
        return generated ?? string.Empty;
    }

    private static string? RunGeneratorAndGetFile(string source, string fileName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new SynapseGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));

        return generatedFile?.GetText().ToString();
    }

    private static (ImmutableArray<Diagnostic> diagnostics, string? generatedSource) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new SynapseGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("RegisterGroup.g.cs", StringComparison.Ordinal));

        return (result.Diagnostics, generatedFile?.GetText().ToString());
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        // Load all trusted platform assemblies (covers System.Runtime, System.Collections, etc.)
        var trustedPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var refs = trustedPaths
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        // Add Synapse.Abstractions (PipelineBehaviorAttribute, IRequest, IEvent, …)
        refs.Add(MetadataReference.CreateFromFile(typeof(PipelineBehaviorAttribute).Assembly.Location));

        // Add UnambitiousFx.Functional (Result<T> used in interface signatures)
        refs.Add(MetadataReference.CreateFromFile(typeof(UnambitiousFx.Functional.Result).Assembly.Location));

        return refs;
    }
}
