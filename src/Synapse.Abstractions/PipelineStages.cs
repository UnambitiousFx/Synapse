namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Maps <see cref="PipelineStage" /> values to the band of <see cref="IOrderedPipelineBehavior.Order" /> values
///     they own. The bands are contiguous and follow the stage order, so sorting by stage then by <c>Order</c> gives
///     the same result as sorting by <c>Order</c> alone when no behavior declares a stage.
/// </summary>
/// <remarks>
///     <list type="table">
///         <listheader><term>Stage</term><description>Order band</description></listheader>
///         <item><term><see cref="PipelineStage.Observability" /></term><description>0 to 0x1FFF_FFFF</description></item>
///         <item><term><see cref="PipelineStage.Authentication" /></term><description>0x2000_0000 to 0x3FFF_FFFF</description></item>
///         <item><term><see cref="PipelineStage.Authorization" /></term><description>0x4000_0000 to 0x5FFF_FFFF</description></item>
///         <item><term><see cref="PipelineStage.Validation" /></term><description>0x6000_0000 to 0x7FFF_FFFF</description></item>
///         <item><term><see cref="PipelineStage.Transaction" /></term><description>0x8000_0000 to 0x9FFF_FFFF</description></item>
///         <item><term><see cref="PipelineStage.Handler" /></term><description>0xA000_0000 to 0xFFFF_FFFF</description></item>
///     </list>
/// </remarks>
public static class PipelineStages
{
    private const uint BandSize = 0x2000_0000;

    /// <summary>
    ///     The first <see cref="IOrderedPipelineBehavior.Order" /> value of a stage's band. A package that only sets
    ///     <c>Order</c> can use <c>StartOf(stage) + offset</c> to land in a stage without declaring it.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The lowest <c>Order</c> that belongs to <paramref name="stage" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage" /> is not a defined stage.</exception>
    public static uint StartOf(PipelineStage stage)
    {
        if (stage is < PipelineStage.Observability or > PipelineStage.Handler)
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown pipeline stage.");
        }

        return (uint)stage * BandSize;
    }

    /// <summary>
    ///     The stage whose band contains <paramref name="order" />: the stage a behavior that declares no stage
    ///     belongs to.
    /// </summary>
    /// <param name="order">An <see cref="IOrderedPipelineBehavior.Order" /> value.</param>
    /// <returns>The stage that owns <paramref name="order" />.</returns>
    public static PipelineStage Of(uint order)
    {
        var band = order / BandSize;
        return band >= (uint)PipelineStage.Handler ? PipelineStage.Handler : (PipelineStage)band;
    }
}
