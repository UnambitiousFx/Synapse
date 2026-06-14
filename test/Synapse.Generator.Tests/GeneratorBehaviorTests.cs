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

    // ── Order attribute controls emission order ───────────────────────────

    [Fact]
    public void BehaviorOrder_LowerValueEmittedFirst()
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

            [PipelineBehavior(Order = 10)]
            public sealed class SecondBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }

            [PipelineBehavior(Order = 1)]
            public sealed class FirstBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;

        // Act (When)
        var generated = RunGeneratorAndGetRegistrationGroup(source);

        // Assert (Then) — FirstBehavior (Order=1) must appear before SecondBehavior (Order=10)
        var firstPos = generated.IndexOf("FirstBehavior", StringComparison.Ordinal);
        var secondPos = generated.IndexOf("SecondBehavior", StringComparison.Ordinal);
        Assert.True(firstPos >= 0, "FirstBehavior registration not found");
        Assert.True(secondPos >= 0, "SecondBehavior registration not found");
        Assert.True(firstPos < secondPos, "FirstBehavior (Order=1) should be emitted before SecondBehavior (Order=10)");
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

    // ── EventDispatcherRegistration.g.cs is generated for IEvent types ───

    [Fact]
    public void EventDispatcherRegistration_IsGenerated()
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
        var generated = RunGeneratorAndGetFile(source, "EventDispatcherRegistration.g.cs");

        // Assert (Then)
        Assert.NotNull(generated);
        Assert.Contains("UserCreated", generated);
        Assert.Contains("EventDispatcherRegistration", generated);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

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
