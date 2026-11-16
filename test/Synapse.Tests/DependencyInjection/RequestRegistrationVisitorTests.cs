using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.DependencyInjection;

public sealed class RequestRegistrationVisitorTests
{
    private static readonly (string Kind, Type Request, Type? Response)[] Expected =
    [
        ("void", typeof(VoidRequest), null),
        ("typed", typeof(TypedRequest), typeof(string)),
        ("stream", typeof(StreamRequest), typeof(int))
    ];

    [Fact]
    public void AddRequestRegistrationVisitor_AfterAddSynapse_VisitsEachRegisteredRequest()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddSynapse(RegisterAll);

        // Act (When)
        services.AddRequestRegistrationVisitor(visitor);

        // Assert (Then)
        Assert.Equal(Expected.OrderBy(v => v.Kind), visitor.Visits.OrderBy(v => v.Kind));
    }

    [Fact]
    public void AddRequestRegistrationVisitor_BeforeAddSynapse_VisitsEachRegisteredRequest()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddRequestRegistrationVisitor(visitor);

        // Act (When)
        services.AddSynapse(RegisterAll);

        // Assert (Then)
        Assert.Equal(Expected.OrderBy(v => v.Kind), visitor.Visits.OrderBy(v => v.Kind));
    }

    [Fact]
    public void AddSynapse_SameRequestInTwoCalls_VisitsItOnce()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddRequestRegistrationVisitor(visitor);

        // Act (When)
        services.AddSynapse(cfg => cfg.RegisterRequestHandler<VoidHandler, VoidRequest>());
        services.AddSynapse(cfg => cfg.RegisterRequestHandler<VoidHandler, VoidRequest>());

        // Assert (Then)
        Assert.Single(visitor.Visits);
    }

    [Fact]
    public void AddRegisterGroup_RequestRegisteredThroughBuilder_IsVisited()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddRequestRegistrationVisitor(visitor);

        // Act (When)
        services.AddSynapse(cfg => cfg.AddRegisterGroup(new Group()));

        // Assert (Then)
        Assert.Equal([("typed", typeof(TypedRequest), typeof(string))], visitor.Visits);
    }

    [Fact]
    public void RegisterRequestHandlerWhen_FalseCondition_DoesNotVisit()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddRequestRegistrationVisitor(visitor);

        // Act (When)
        services.AddSynapse(cfg => cfg.RegisterRequestHandlerWhen<VoidHandler, VoidRequest>(() => false));

        // Assert (Then)
        Assert.Empty(visitor.Visits);
    }

    [Fact]
    public void RegisterRequestHandlerWhen_BuilderFalseCondition_DoesNotVisit()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddRequestRegistrationVisitor(visitor);

        // Act (When)
        services.AddSynapse(cfg => cfg.AddRegisterGroup(new WhenGroup()));

        // Assert (Then)
        Assert.Empty(visitor.Visits);
    }

    [Fact]
    public void RegisterRequestHandlerWhen_TrueCondition_Visits()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddRequestRegistrationVisitor(visitor);

        // Act (When)
        services.AddSynapse(cfg => cfg.RegisterRequestHandlerWhen<VoidHandler, VoidRequest>(() => true));

        // Assert (Then)
        Assert.Equal([("void", typeof(VoidRequest), (Type?)null)], visitor.Visits);
    }

    [Fact]
    public void RegisterEventHandler_IsNotVisited()
    {
        // Arrange (Given)
        var visitor = new RecordingVisitor();
        var services = new ServiceCollection().AddRequestRegistrationVisitor(visitor);

        // Act (When)
        services.AddSynapse(cfg => cfg.RegisterEventHandler<EventHandler, TestEvent>());

        // Assert (Then)
        Assert.Empty(visitor.Visits);
    }

    private static void RegisterAll(ISynapseConfig cfg)
    {
        cfg.RegisterRequestHandler<VoidHandler, VoidRequest>();
        cfg.RegisterRequestHandler<TypedHandler, TypedRequest, string>();
        cfg.RegisterStreamRequestHandler<StreamHandler, StreamRequest, int>();
    }

    private sealed class RecordingVisitor : IRequestRegistrationVisitor
    {
        public List<(string Kind, Type Request, Type? Response)> Visits { get; } = [];

        public void VisitRequest<TRequest>() where TRequest : IRequest
        {
            Visits.Add(("void", typeof(TRequest), null));
        }

        public void VisitRequest<TRequest, TResponse>() where TRequest : IRequest<TResponse> where TResponse : notnull
        {
            Visits.Add(("typed", typeof(TRequest), typeof(TResponse)));
        }

        public void VisitStreamRequest<TRequest, TItem>() where TRequest : IStreamRequest<TItem> where TItem : notnull
        {
            Visits.Add(("stream", typeof(TRequest), typeof(TItem)));
        }
    }

    private sealed class Group : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterRequestHandler<TypedHandler, TypedRequest, string>();
        }
    }

    private sealed class WhenGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterRequestHandlerWhen<VoidHandler, VoidRequest>(() => false);
            builder.RegisterStreamRequestHandlerWhen<StreamHandler, StreamRequest, int>(() => false);
        }
    }

    private sealed record VoidRequest : IRequest;

    private sealed record TypedRequest : IRequest<string>;

    private sealed record StreamRequest : IStreamRequest<int>;

    private sealed record TestEvent : IEvent;

    private sealed class VoidHandler : IRequestHandler<VoidRequest>
    {
        public ValueTask<Result> HandleAsync(VoidRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class TypedHandler : IRequestHandler<TypedRequest, string>
    {
        public ValueTask<Result<string>> HandleAsync(TypedRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success("x"));
        }
    }

    private sealed class StreamHandler : IStreamRequestHandler<StreamRequest, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(StreamRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return Result.Success(1);
        }
    }

    private sealed class EventHandler : IEventHandler<TestEvent>
    {
        public ValueTask<Result> HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }
}
