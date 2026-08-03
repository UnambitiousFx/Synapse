using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class ContextHandler : IContextAccessor, IDisposable
{
    private readonly IContextFactory _contextFactory;
    private readonly object _gate = new();
    private readonly IInboundContextStore _inboundContextStore;
    private IContext? _context;
    private IContext? _displaced;
    private bool _published;

    public ContextHandler(IContextFactory contextFactory,
        IInboundContextStore inboundContextStore)
    {
        _contextFactory = contextFactory;
        _inboundContextStore = inboundContextStore;
    }

    public bool IsInitialized => Volatile.Read(ref _context) is not null;

    public IContext Context
    {
        get
        {
            var context = Volatile.Read(ref _context) ?? Create();

            if (!ReferenceEquals(AmbientContext.Value, context))
            {
                // Another execution branch created the context, and an AsyncLocal write does not cross into a
                // sibling branch, so this one publishes for itself. Nothing to restore: the branch is discarded
                // when its work completes, and the scope's own restore is recorded in Create.
                AmbientContext.Exchange(context);
            }

            return context;
        }
    }

    /// <summary>
    ///     Restores whatever context was ambient before this scope published its own.
    /// </summary>
    /// <remarks>
    ///     Runs when the scope is disposed, which is what keeps a nested unit of work from outliving its scope in
    ///     the ambient slot. A scope that never had its context read publishes nothing and so restores nothing.
    /// </remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (!_published)
            {
                return;
            }

            _published = false;
            AmbientContext.Exchange(_displaced);
            _displaced = null;
        }
    }

    /// <summary>
    ///     Builds the context for this scope, exactly once.
    /// </summary>
    /// <remarks>
    ///     The lock is not decoration. An event published to several handlers runs them concurrently against one
    ///     scope, so two of them reading <see cref="Context" /> could each find the field empty, each build a
    ///     context with its own freshly minted trace id, and each go on using the instance it built while only one
    ///     won the field — the same identity divergence known issue 029 removed, reintroduced through the accessor
    ///     rather than through DI (see known issue 036).
    /// </remarks>
    private IContext Create()
    {
        lock (_gate)
        {
            if (_context is not null)
            {
                return _context;
            }

            // Built once, from whatever the boundary adapter extracted. Because the inbound state is read
            // here rather than pushed into an existing context, ordering between the adapter and the first
            // consumer of IContext does not matter.
            var created = _contextFactory.Create(_inboundContextStore.Inbound);

            // Mirrored onto the execution context for the one consumer DI cannot serve — see AmbientContext.
            _displaced = AmbientContext.Exchange(created);
            _published = true;
            Volatile.Write(ref _context, created);
            return created;
        }
    }
}
