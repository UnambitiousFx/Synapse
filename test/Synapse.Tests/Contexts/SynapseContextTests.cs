using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;

namespace UnambitiousFx.Synapse.Tests.Contexts;

/// <summary>
///     Covers the public read side of the ambient slot, which transport integrations outside this assembly
///     use when the library hands them no DI scope.
/// </summary>
public sealed class SynapseContextTests
{
    [Fact]
    public void Current_WithNoUnitOfWork_IsNull()
    {
        // Arrange (Given) — nothing has published a context on this branch

        // Act (When)
        var current = SynapseContext.Current;

        // Assert (Then) — a transport hook reads this as "do not propagate" rather than inventing a flow
        Assert.Null(current);
    }

    [Fact]
    public void Current_WhenTheContextWasRead_IsThatContext()
    {
        // Arrange (Given)
        using var handler = NewHandler();

        // Act (When)
        var context = handler.Context;

        // Assert (Then)
        Assert.Same(context, SynapseContext.Current);
    }

    [Fact]
    public void Current_WhenTheContextWasNeverRead_StaysNull()
    {
        // Arrange (Given) — a scope that never touched the mediator has no flow to expose
        using var handler = NewHandler();

        // Act (When)
        var current = SynapseContext.Current;

        // Assert (Then)
        Assert.Null(current);
    }

    [Fact]
    public void Current_AfterTheScopeEnds_IsRestored()
    {
        // Arrange (Given) — scopes nest, and an inner unit of work must not outlive its scope
        using var outer = NewHandler();
        var outerContext = outer.Context;

        // Act (When)
        var inner = NewHandler();
        _ = inner.Context;
        inner.Dispose();

        // Assert (Then)
        Assert.Same(outerContext, SynapseContext.Current);
    }

    private static ContextHandler NewHandler()
    {
        return new ContextHandler(new DefaultContextFactory(),
            new InboundContextStore { Inbound = PropagatedContext.None });
    }
}
