using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>
///     An <see cref="IContextAccessor" /> whose initialization state is set explicitly, so a test can distinguish
///     "no context exists" from "a context exists" without standing up a DI scope.
/// </summary>
/// <remarks>
///     <see cref="ContextWasRead" /> is what makes the distinction assertable: production code is expected to
///     check <see cref="IsInitialized" /> and leave <see cref="Context" /> alone when no unit of work is running,
///     because reading it is what creates a context.
/// </remarks>
public sealed class StubContextAccessor : IContextAccessor
{
    private readonly IContext? _context;

    public StubContextAccessor(IContext? context,
        bool isInitialized)
    {
        _context = context;
        IsInitialized = isInitialized;
    }

    public bool ContextWasRead { get; private set; }

    public bool IsInitialized { get; }

    public IContext Context
    {
        get
        {
            ContextWasRead = true;
            return _context ?? throw new InvalidOperationException(
                "The test accessor was asked for a context it was not given one.");
        }
    }
}
