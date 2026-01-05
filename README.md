# Synapse

[![Build Status](https://github.com/UnambitiousFx/Synapse/workflows/CI/badge.svg)](https://github.com/UnambitiousFx/Synapse/actions)
[![NuGet](https://img.shields.io/nuget/v/UnambitiousFx.Synapse.svg)](https://www.nuget.org/packages/UnambitiousFx.Synapse/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/UnambitiousFx.Synapse.svg)](https://www.nuget.org/packages/UnambitiousFx.Synapse/)
[![codecov](https://codecov.io/gh/UnambitiousFx/Synapse/branch/main/graph/badge.svg)](https://codecov.io/gh/UnambitiousFx/Synapse)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)

A lightweight, **high-performance** mediator implementation for .NET with first-class integration with `Result` types.

---

## 🔧 Compatibility & support

- **Dependency-free at runtime:** No external runtime dependencies.
- **Native AOT-compatible:** Designed to work well in Native AOT scenarios (see the `examples/WebApiAot`).
- **Supported .NET versions:** Supports Microsoft LTS releases and the latest non-LTS release. See CI matrix for exact versions.

## 🎯 Features

- **Lightweight Mediator** — Requests, commands, queries, and notifications with minimal allocations.
- **Result-first** — Uses `UnambitiousFx.Functional` `Result` for explicit error handling.
- **Streaming requests** — Built-in support for streaming request/response patterns.
- **Pipeline Behaviors** — Typed, untyped and conditional pipeline behaviors for requests and events.
- **Dependency injection friendly** — Register handlers and behaviours via a fluent configuration API (`AddMediator`).
- **Outbox support** — Interfaces and helpers to implement the outbox pattern and reliable event publishing.
- **Observability hooks** — Metrics and tracing integration points to capture latency and publish metrics.
- **Source generator** — Optional code-generation to reduce allocations and simplify registration.
- **Examples & benchmarks** — Real-world examples and performance benchmarks included.

## 📦 Installation

```bash
dotnet add package UnambitiousFx.Synapse
```

## 🚀 Quick Start

### Register mediator services

Register the mediator and your handlers in `Program.cs`:

```csharp
builder.Services.AddMediator(cfg =>
{
    cfg.AddRegisterGroup(new ManualRegisterGroup());

    // Request handlers
    cfg.RegisterRequestHandler<CreateTodoCommandHandler, CreateTodoCommand, Guid>()
        .RegisterRequestHandler<ListTodoQueryHandler, ListTodoQuery, IEnumerable<Todo>>();

    // Event handlers
    cfg.RegisterEventHandler<TodoUpdatedHandler, TodoUpdated>();

    // Pipeline behaviors
    cfg.RegisterRequestPipelineBehavior<SimpleLoggingBehavior>();
    cfg.RegisterEventPipelineBehavior<SimpleLoggingBehavior>();
});
```

### Send requests

Use `ISender` to dispatch requests to handlers:

```csharp
// Send a command that returns a value
var result = await sender.SendAsync<CreateTodoCommand, Guid>(command);

// Send a command without a response
var result = await sender.SendAsync<UpdateTodoCommand>(command);

// Stream results from an IStreamRequest
await foreach (var itemResult in sender.SendStreamAsync<ListItemsRequest, Item>(request))
{
    // itemResult is Result<Item>
}
```

### Use handlers directly

You can also resolve `IRequestHandler<TRequest, TResponse>` or `IRequestHandler<TRequest>` from DI and call `HandleAsync` directly when appropriate.

## 📊 Observability & Metrics

Synapse exposes hooks for recording metrics and integrates with OpenTelemetry tracing through dedicated activity sources and metric interfaces. Consumers can provide their own `IMediatorMetrics` implementation or use the default which integrates with `IMeterFactory`.

## 🧪 Examples & Benchmarks

- Examples are under the `examples/` folder (Web API, Console, Native AOT example).
- Benchmarks are available in `benchmarks/SynapseBenchmark` to measure throughput and compare against alternatives.

## 🧩 Extensibility

- **Pipeline behaviors:** implement `IRequestPipelineBehavior`, `IEventPipelineBehavior`, or the typed/stream variants.
- **Registration groups:** implement `IRegisterGroup` to modularize and share handler registration logic.
- **Outbox & commits:** implement `IOutboxStorage`, `IOutboxCommit` for transactional event persistence.

> Note: Transport/distributed messaging APIs are intentionally not documented here — they may change prior to the first stable release.

## 🤝 Contributing

We welcome contributions! Please read `CONTRIBUTING.md` for standards, development setup, and the PR process.

## 📝 Release notes

See releases on GitHub for detailed changelogs and version history: https://github.com/UnambitiousFx/Synapse/releases

## 📄 License

This project is licensed under the MIT License - see the `LICENSE` file for details.

---

Made with ❤️ by the UnambitiousFx team
