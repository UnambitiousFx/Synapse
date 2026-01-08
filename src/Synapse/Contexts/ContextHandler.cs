using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class ContextHandler(IContextFactory contextFactory) : IContextAccessor, IContextSetter
{
    public IContext Context
    {
        get
        {
            if (field == null)
            {
                field = contextFactory.Create();
                CorrelationContext.CurrentCorrelationId = field.CorrelationId;
            }

            return field;
        }
        set
        {
            field = value;
            CorrelationContext.CurrentCorrelationId = value.CorrelationId;
        }
    }
}