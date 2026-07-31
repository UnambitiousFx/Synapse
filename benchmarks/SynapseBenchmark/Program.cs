using BenchmarkDotNet.Running;
using UnambitiousFx.Benchmarks.SynapseBenchmark;

// Run benchmarks comparing UnambitiousFx Synapse vs MediatR vs Mediator (martinothamar)
BenchmarkRunner.Run<SynapseVsMediatorsBenchmarks>();

// Context creation and cross-boundary propagation — both on the per-message hot path
BenchmarkRunner.Run<PropagationBenchmarks>();