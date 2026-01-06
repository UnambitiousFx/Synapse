using BenchmarkDotNet.Running;
using UnambitiousFx.Benchmarks.SynapseBenchmark;

// Run benchmarks comparing UnambitiousFx Synapse vs MediatR
BenchmarkRunner.Run<MediatorVsMediatRBenchmarks>();