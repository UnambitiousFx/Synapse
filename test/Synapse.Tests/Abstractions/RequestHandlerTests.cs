using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Tests.Abstractions;

public sealed class RequestHandlerTests
{
    [Fact]
    public async Task GenericRequestHandler_HandleAsync_UsesSynchronousHandle()
    {
        // Arrange (Given)
        var handler = new IntRequestHandler();

        // Act (When)
        var result = await handler.HandleAsync(new IntRequest(5), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Match(v => v, _ => -1));
    }

    [Fact]
    public async Task VoidRequestHandler_HandleAsync_UsesSynchronousHandle()
    {
        // Arrange (Given)
        var handler = new VoidRequestHandler();

        // Act (When)
        var result = await handler.HandleAsync(new VoidRequest(true), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void MissingHandlerException_ContainsRequestTypeInMessage()
    {
        // Arrange (Given)
        var exception = new MissingHandlerException(typeof(IntRequest));

        // Assert (Then)
        Assert.Contains(nameof(IntRequest), exception.Message, StringComparison.Ordinal);
    }

    private sealed record IntRequest(int Value) : IRequest<int>;

    private sealed class IntRequestHandler : RequestHandler<IntRequest, int>
    {
        protected override Result<int> Handle(IntRequest request)
        {
            return Result.Success(request.Value + 1);
        }
    }

    private sealed record VoidRequest(bool IsValid) : IRequest;

    private sealed class VoidRequestHandler : RequestHandler<VoidRequest>
    {
        protected override Result Handle(VoidRequest request)
        {
            return request.IsValid ? Result.Success() : Result.Failure("invalid");
        }
    }
}
