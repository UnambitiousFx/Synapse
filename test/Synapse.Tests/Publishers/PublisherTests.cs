using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publishers;

[TestSubject(typeof(Publisher))]
public sealed class PublisherTests
{
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IOutboxManager _outboxManager;
    private readonly Publisher _publisher;

    public PublisherTests()
    {
        _eventDispatcher = Substitute.For<IEventDispatcher>();
        _outboxManager = Substitute.For<IOutboxManager>();

        _publisher = new Publisher(
            _eventDispatcher,
            _outboxManager,
            Options.Create(new PublisherOptions())
        );
    }

    [Fact]
    public async Task GivenAnEvent_WhenPublish_ShouldDispatchEvent()
    {
        var @event = new EventExample("Event 1");
        _eventDispatcher.DispatchAsync(@event, DistributionMode.Undefined, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _publisher.PublishAsync(
            @event,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenAnEvent_WhenPublishWithOutbox_ShouldDispatchEvent()
    {
        _outboxManager.StoreAsync(Arg.Any<IEvent>(), Arg.Any<DistributionMode>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var @event = new EventExample("Event 1");
        _eventDispatcher.DispatchAsync(@event, DistributionMode.Local, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _publisher.PublishAsync(
            @event,
            PublishMode.Outbox,
            DistributionMode.Local,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenOutboxEvent_WhenCommit_ShouldDelegateToOutboxManager()
    {
        var @event = new EventExample("Event 1");
        _outboxManager.ProcessPendingAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var outboxCommit = new OutboxCommit(_outboxManager);
        var result = await outboxCommit.CommitAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _outboxManager.Received(1).ProcessPendingAsync(Arg.Any<CancellationToken>());
    }
}