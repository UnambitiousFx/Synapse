using BenchmarkDotNet.Running;
using UnambitiousFx.Benchmarks.SynapseBenchmark;

// Run benchmarks comparing UnambitiousFx Mediator vs MediatR
BenchmarkRunner.Run<MediatorVsMediatRBenchmarks>();