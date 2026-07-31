using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Propagation;

namespace UnambitiousFx.Benchmarks.SynapseBenchmark;

/// <summary>
///     Measures context creation and the inject/extract pair, both of which sit on the per-message hot path.
/// </summary>
[MemoryDiagnoser]
public class PropagationBenchmarks
{
    private Dictionary<string, string> _populatedHeaders = null!;
    private PropagatedContext _inbound;
    private IContext _context = null!;
    private IContextFactory _contextFactory = null!;
    private IContextPropagator _propagator = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    [GlobalSetup]
    public void Setup()
    {
        _serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddSynapse(_ => { })
            .BuildServiceProvider();

        _scope = _serviceProvider.CreateScope();
        _contextFactory = _scope.ServiceProvider.GetRequiredService<IContextFactory>();
        _propagator = _serviceProvider.GetRequiredService<IContextPropagator>();

        _context = _contextFactory.Create(PropagatedContext.None);
        _context.SetBaggage("tenant.id", "contoso");
        _context.SetBaggage("user.id", "u-42");

        var carrier = new DictionaryPropagationCarrier();
        _propagator.Inject(_context, carrier);
        _populatedHeaders = carrier.Headers.ToDictionary(h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase);
        _inbound = _propagator.Extract(new DictionaryPropagationCarrier(_populatedHeaders));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    [Benchmark(Baseline = true)]
    public IContext CreateContext_Root()
    {
        // No inbound trace context and no ambient activity, so this mints a trace id.
        return _contextFactory.Create(PropagatedContext.None);
    }

    [Benchmark]
    public IContext CreateContext_FromInboundTrace()
    {
        return _contextFactory.Create(_inbound);
    }

    [Benchmark]
    public IReadOnlyDictionary<string, string> Inject_WithoutActivity()
    {
        var carrier = new DictionaryPropagationCarrier();
        _propagator.Inject(_context, carrier);
        return carrier.Headers;
    }

    [Benchmark]
    public IReadOnlyDictionary<string, string> Inject_WithActivity()
    {
        // With an activity current, the platform propagator also writes traceparent/tracestate.
        using var activity = new Activity("bench");
        activity.Start();

        var carrier = new DictionaryPropagationCarrier();
        _propagator.Inject(_context, carrier);
        return carrier.Headers;
    }

    [Benchmark]
    public PropagatedContext Extract()
    {
        return _propagator.Extract(new DictionaryPropagationCarrier(_populatedHeaders));
    }

    [Benchmark]
    public IContext SetBaggage_FirstEntry()
    {
        // The baggage store is created on first write, so this pays for the dictionary the empty-baggage
        // path avoids. Also the contended-in-principle path: mutation is serialized under a lock.
        var context = _contextFactory.Create(PropagatedContext.None);
        context.SetBaggage("tenant.id", "contoso");
        return context;
    }

    [Benchmark]
    public string? SetBaggage_Overwrite_ThenRead()
    {
        _context.SetBaggage("tenant.id", "fabrikam");
        return _context.GetBaggage("tenant.id");
    }

    [Benchmark]
    public IContextFeature? SetFeature_ThenRead()
    {
        // Features are what the CQRS boundary behavior sets per request, and the store is likewise created
        // on first write.
        var context = _contextFactory.Create(PropagatedContext.None);
        context.SetFeature(new BenchFeature());
        return context.GetFeature<BenchFeature>();
    }

    [Benchmark]
    public PropagatedContext RoundTrip()
    {
        var carrier = new DictionaryPropagationCarrier();
        _propagator.Inject(_context, carrier);
        return _propagator.Extract(carrier);
    }

    private sealed class BenchFeature : IContextFeature
    {
        public string Name => "Bench";
    }
}
