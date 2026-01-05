using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class ContextAccessor(IContextFactory contextFactory) : IContextAccessor
{
    private IContext? _context;

    public IContext Context
    {
        get
        {
            if (_context == null)
            {
                _context = contextFactory.Create();
                CorrelationContext.CurrentCorrelationId = _context.CorrelationId;
            }

            return _context;
        }
        set
        {
            _context = value;
            CorrelationContext.CurrentCorrelationId = value.CorrelationId;
        }
    }
}