using JetBrains.Annotations;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Abstractions;

[TestSubject(typeof(ICommand))]
public sealed class CqrsMarkerShapeTests
{
    [Fact]
    public void ICommand_IsARequest()
    {
        // Arrange (Given)
        var command = typeof(ICommand);

        // Act (When)
        var isRequest = typeof(IRequest).IsAssignableFrom(command);

        // Assert (Then)
        Assert.True(isRequest);
    }

    [Fact]
    public void ICommandOfT_IsARequestOfT()
    {
        // Arrange (Given)
        var command = typeof(ICommand<int>);

        // Act (When)
        var isRequest = typeof(IRequest<int>).IsAssignableFrom(command);

        // Assert (Then)
        Assert.True(isRequest);
    }

    [Fact]
    public void IQueryOfT_IsARequestOfT()
    {
        // Arrange (Given)
        var query = typeof(IQuery<int>);

        // Act (When)
        var isRequest = typeof(IRequest<int>).IsAssignableFrom(query);

        // Assert (Then)
        Assert.True(isRequest);
    }

    [Fact]
    public void ICommandOfT_IsNotAQuery()
    {
        // Arrange (Given)
        var command = typeof(ICommand<int>);

        // Act (When)
        var isQuery = typeof(IQuery<int>).IsAssignableFrom(command);

        // Assert (Then)
        Assert.False(isQuery);
    }

    [Fact]
    public void HandlerAliases_InheritTheMatchingRequestHandler()
    {
        // Arrange (Given)
        var voidHandler = typeof(ICommandHandler<VoidCommand>);
        var commandHandler = typeof(ICommandHandler<CreateCommand, int>);
        var queryHandler = typeof(IQueryHandler<GetQuery, int>);

        // Act (When)
        var voidInherits = typeof(IRequestHandler<VoidCommand>).IsAssignableFrom(voidHandler);
        var commandInherits = typeof(IRequestHandler<CreateCommand, int>).IsAssignableFrom(commandHandler);
        var queryInherits = typeof(IRequestHandler<GetQuery, int>).IsAssignableFrom(queryHandler);

        // Assert (Then)
        Assert.True(voidInherits);
        Assert.True(commandInherits);
        Assert.True(queryInherits);
    }

    [Fact]
    public void ICommandOfT_IsCovariantInTheResponse()
    {
        // Arrange (Given)
        ICommand<string> narrow = new NamedCommand();

        // Act (When)
        ICommand<object> wide = narrow;

        // Assert (Then)
        Assert.Same(narrow, wide);
    }

    [Fact]
    public void IQueryOfT_IsCovariantInTheResponse()
    {
        // Arrange (Given)
        IQuery<string> narrow = new NamedQuery();

        // Act (When)
        IQuery<object> wide = narrow;

        // Assert (Then)
        Assert.Same(narrow, wide);
    }

    [Fact]
    public void ICommandHandler_IsContravariantInTheCommand()
    {
        // Arrange (Given)
        ICommandHandler<BaseCommand, int> baseHandler = new BaseCommandHandler();

        // Act (When)
        ICommandHandler<DerivedCommand, int> derivedHandler = baseHandler;

        // Assert (Then)
        Assert.Same(baseHandler, derivedHandler);
    }

    private sealed record VoidCommand : ICommand;

    private sealed record CreateCommand : ICommand<int>;

    private sealed record GetQuery : IQuery<int>;

    private sealed record NamedCommand : ICommand<string>;

    private sealed record NamedQuery : IQuery<string>;

    private record BaseCommand : ICommand<int>;

    private sealed record DerivedCommand : BaseCommand;

    private sealed class BaseCommandHandler : ICommandHandler<BaseCommand, int>
    {
        public ValueTask<Result<int>> HandleAsync(BaseCommand request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success(1));
        }
    }
}
