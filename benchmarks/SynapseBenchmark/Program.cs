using BenchmarkDotNet.Running;
using UnambitiousFx.Benchmarks.SynapseBenchmark;

// Arguments are forwarded so a single suite can be picked, e.g. `-- --filter *PipelineOrdering*`.

// Run benchmarks comparing UnambitiousFx Synapse vs MediatR vs Mediator (martinothamar)
BenchmarkRunner.Run<SynapseVsMediatorsBenchmarks>(args: args);

// Context creation and cross-boundary propagation — both on the per-message hot path
BenchmarkRunner.Run<PropagationBenchmarks>(args: args);

// Relative behavior ordering (stages, Before/After) against Order-only pipelines
BenchmarkRunner.Run<PipelineOrderingBenchmarks>(args: args);
