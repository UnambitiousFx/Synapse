using System.Reflection;
using BenchmarkDotNet.Running;

// BenchmarkSwitcher discovers every benchmark class in this assembly (the Synapse-vs-MediatR/Mediator
// comparison, context propagation, and endpoint dispatch) and honors command-line filters, e.g.
// `dotnet run -c Release --project benchmarks/SynapseBenchmark -- --filter '*EndpointDispatch*'`.
// With no filter it falls back to its interactive picker.
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);