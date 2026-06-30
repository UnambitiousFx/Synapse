using BenchmarkDotNet.Running;
using UnambitiousFx.Benchmarks.SynapseBenchmark;

// Run benchmarks comparing UnambitiousFx Synapse vs MediatR vs Mediator (martinothamar)
BenchmarkRunner.Run<SynapseVsMediatorsBenchmarks>();