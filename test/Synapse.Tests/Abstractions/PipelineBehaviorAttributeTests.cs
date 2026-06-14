using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Abstractions;

public sealed class PipelineBehaviorAttributeTests
{
    [Fact]
    public void PipelineBehaviorAttribute_DefaultOrder_IsZero()
    {
        // Arrange / Act (Given / When)
        var attr = new PipelineBehaviorAttribute();

        // Assert (Then)
        Assert.Equal(0, attr.Order);
    }

    [Fact]
    public void PipelineBehaviorAttribute_CustomOrder_ReturnsSetValue()
    {
        // Arrange / Act (Given / When)
        var attr = new PipelineBehaviorAttribute { Order = 5 };

        // Assert (Then)
        Assert.Equal(5, attr.Order);
    }
}
