using System.Diagnostics;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;
using UnambitiousFx.Synapse.Contexts;

namespace UnambitiousFx.Synapse.Tests.Contexts;

public sealed class ContextTests
{
    [Fact]
    public async Task Context_MetadataAndFeatureApis_WorkAsExpected()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        var correlationId = Guid.NewGuid();
        var context = new Context(emitter, outboxCommit, correlationId);

        var feature = new TestFeature("v1");

        // Act (When)
        context.SetMetadata("k", "v");
        var gotByTryGet = context.TryGetMetadata<string>("k", out var metadataValue);
        var gotByGet = context.GetMetadata<string>("k");
        var removed = context.RemoveMetadata("k");
        var removedAgain = context.RemoveMetadata("k");

        context.SetFeature(feature);
        var hasFeature = context.TryGetFeature<TestFeature>(out var tryFeature);
        var getFeature = context.GetFeature<TestFeature>();
        var mustFeature = context.MustGetFeature<TestFeature>();
        context.RemoveFeature<TestFeature>();

        // Assert (Then)
        Assert.Equal(correlationId, context.CorrelationId);
        Assert.True(gotByTryGet);
        Assert.Equal("v", metadataValue);
        Assert.Equal("v", gotByGet);
        Assert.True(removed);
        Assert.False(removedAgain);

        Assert.True(hasFeature);
        Assert.Same(feature, tryFeature);
        Assert.Same(feature, getFeature);
        Assert.Same(feature, mustFeature);
        Assert.Null(context.GetFeature<TestFeature>());
        Assert.False(context.TryGetFeature<TestFeature>(out _));
    }

    [Fact]
    public async Task Context_PublishAndCommit_DelegatesToDependencies()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        var context = new Context(emitter, outboxCommit, Guid.NewGuid());
        var @event = new TestEvent();

        emitter.EmitAsync(@event, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));
        emitter.EmitAsync(@event, EmitMode.Outbox, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));
        outboxCommit.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        // Act (When)
        var publishResult = await context.PublishEventAsync(@event, TestContext.Current.CancellationToken);
        var publishWithModeResult = await context.PublishEventAsync(@event, EmitMode.Outbox, TestContext.Current.CancellationToken);
        var commitResult = await context.CommitEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(publishResult.IsSuccess);
        Assert.True(publishWithModeResult.IsSuccess);
        Assert.True(commitResult.IsSuccess);
        await emitter.Received(1).EmitAsync(@event, TestContext.Current.CancellationToken);
        await emitter.Received(1).EmitAsync(@event, EmitMode.Outbox, TestContext.Current.CancellationToken);
        await outboxCommit.Received(1).CommitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Context_WithCorrelationId_ReturnsNewContextWithUpdatedId()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        var original = new Context(emitter, outboxCommit, Guid.NewGuid());
        var updatedCorrelationId = Guid.NewGuid();

        // Act (When)
        var updated = original.WithCorrelationId(updatedCorrelationId);

        // Assert (Then)
        Assert.Equal(updatedCorrelationId, updated.CorrelationId);
        Assert.NotEqual(original.CorrelationId, updated.CorrelationId);
    }

    [Fact]
    public void Context_MustGetFeature_WhenMissing_Throws()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        var context = new Context(emitter, outboxCommit, Guid.NewGuid());

        // Act (When)
        var action = () => context.MustGetFeature<TestFeature>();

        // Assert (Then)
        var exception = Assert.Throws<MissingContextFeatureException>(action);
        Assert.Contains(nameof(TestFeature), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_CapturesTraceMetadata_WhenActivityIsPresent()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        using var activity = new Activity("synapse-test");
        activity.AddBaggage("tenant", "contoso");
        activity.Start();

        // Act (When)
        var context = new Context(emitter, outboxCommit, Guid.NewGuid());

        // Assert (Then)
        Assert.True(context.TryGetMetadata<string>("Tracing.TraceId", out var traceId));
        Assert.True(context.TryGetMetadata<string>("Tracing.SpanId", out var spanId));
        Assert.Equal(activity.TraceId.ToString(), traceId);
        Assert.Equal(activity.SpanId.ToString(), spanId);
        Assert.True(context.TryGetMetadata<string>("Tracing.Baggage.tenant", out var tenant));
        Assert.Equal("contoso", tenant);
    }

    [Fact]
    public void Context_DoesNotCaptureParentSpanId_WhenParentIsDefault()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        using var activity = new Activity("synapse-test");
        activity.Start();

        // Act (When)
        var context = new Context(emitter, outboxCommit, Guid.NewGuid());

        // Assert (Then)
        Assert.Equal(default, activity.ParentSpanId);
        Assert.False(context.TryGetMetadata<string>("Tracing.ParentSpanId", out _));
    }

    [Fact]
    public async Task SlimContextFactory_Create_ReturnsContextWithWorkingDependencies()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        var factory = new SlimContextFactory(emitter, outboxCommit);
        var @event = new TestEvent();

        emitter.EmitAsync(@event, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));
        outboxCommit.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        // Act (When)
        var context = factory.Create();
        var publishResult = await context.PublishEventAsync(@event, TestContext.Current.CancellationToken);
        var commitResult = await context.CommitEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotEqual(Guid.Empty, context.CorrelationId);
        Assert.True(publishResult.IsSuccess);
        Assert.True(commitResult.IsSuccess);
    }

    [Fact]
    public void Context_TryGetMetadata_WhenTypeMismatch_ReturnsFalse()
    {
        // Arrange (Given)
        var context = new Context(Substitute.For<IEmitter>(), Substitute.For<IOutboxCommit>(), Guid.NewGuid());
        context.SetMetadata("key", "string-value");

        // Act (When)
        var found = context.TryGetMetadata<int>("key", out var value);

        // Assert (Then)
        Assert.False(found);
        Assert.Equal(default, value);
    }

    [Fact]
    public void Context_GetMetadata_WhenKeyMissing_ReturnsDefault()
    {
        // Arrange (Given)
        var context = new Context(Substitute.For<IEmitter>(), Substitute.For<IOutboxCommit>(), Guid.NewGuid());

        // Act (When)
        var value = context.GetMetadata<string>("nonexistent");

        // Assert (Then)
        Assert.Null(value);
    }

    [Fact]
    public void Context_CopyConstructor_MergesMetadataAndFeatures()
    {
        // Arrange (Given)
        var emitter = Substitute.For<IEmitter>();
        var outboxCommit = Substitute.For<IOutboxCommit>();
        var original = new Context(emitter, outboxCommit, Guid.NewGuid());
        original.SetMetadata("orig-key", "orig-val");
        var feature = new TestFeature("f1");

        var extraMeta = new Dictionary<string, object> { ["new-key"] = "new-val" };
        var extraFeatures = new Dictionary<Type, IContextFeature> { [typeof(TestFeature)] = feature };

        // Act (When) — copy constructor merges provided dicts with existing ones
        var copy = new Context(original, extraFeatures, extraMeta);

        // Assert (Then)
        Assert.True(copy.TryGetMetadata<string>("new-key", out var newVal));
        Assert.Equal("new-val", newVal);
        Assert.Equal(original.CorrelationId, copy.CorrelationId);
        Assert.Same(feature, copy.GetFeature<TestFeature>());
    }

    private sealed record TestEvent : IEvent;

    private sealed record TestFeature(string Value) : IContextFeature
    {
        public string Name => "TestFeature";
    }
}
