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
- **Native AOT-compatible:** Designed to work well in Native AOT scenarios (see the `examples/MinimalApi`).
- **Supported .NET versions:** Supports Microsoft LTS releases and the latest non-LTS release. See CI matrix for exact
  versions.

## 🎯 Features

- **Lightweight Mediator** — Requests, commands, queries, and notifications with minimal allocations.
- **Result-first** — Uses `UnambitiousFx.Functional` `Result` for explicit error handling.
- **Streaming requests** — Built-in support for streaming request/response patterns.
- **Pipeline Behaviors** — Typed, untyped and conditional pipeline behaviors for requests and events.
- **Dependency injection friendly** — Register handlers and behaviours via a fluent configuration API (`AddSynapse`).
- **Outbox support** — Interfaces and helpers to implement the outbox pattern and reliable event publishing.
- **Observability hooks** — Metrics and tracing integration points to capture latency and publish metrics.
- **Source generator** — Optional code-generation to reduce allocations and simplify registration.
- **Endpoints** (`UnambitiousFx.Synapse.Endpoints`) — Optional endpoint-per-class HTTP layer that maps routes to commands/queries with generated binding and full Native AOT support. See the [Endpoints docs](https://unambitiousfx.com/lib-synapse/endpoints).
- **Examples & benchmarks** — Real-world examples and performance benchmarks included.

## 📦 Installation

```bash
dotnet add package UnambitiousFx.Synapse
```

## 🚀 Quick Start

### Register mediator services

Register the synapse and your handlers in `Program.cs`:

```csharp
builder.Services.AddSynapse(cfg =>
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

Use `IInvoker` to dispatch requests to handlers:

```csharp
// Send a command that returns a value
var result = await invoker.InvokeAsync<CreateTodoCommand, Guid>(command);

// Send a command without a response
var result = await invoker.InvokeAsync<UpdateTodoCommand>(command);

// Stream results from an IStreamRequest
await foreach (var itemResult in invoker.InvokeStreamAsync<ListItemsRequest, Item>(request))
{
    // itemResult is Result<Item>
}
```

### Use handlers directly

You can also resolve `IRequestHandler<TRequest, TResponse>` or `IRequestHandler<TRequest>` from DI and call
`HandleAsync` directly when appropriate.

## 📊 Observability & Metrics

Synapse exposes hooks for recording metrics and integrates with OpenTelemetry tracing through dedicated activity sources
and metric interfaces. Consumers can provide their own `ISynapseMetrics` implementation or use the default which
integrates with `IMeterFactory`.

## 📚 Documentation

Full documentation is available at https://unambitiousfx.com/lib-synapse/

## 🧪 Examples & Benchmarks

- Examples are under the `examples/` folder (Web API, Console, Native AOT example).
- Benchmarks are available in `benchmarks/SynapseBenchmark` to measure throughput and compare against alternatives.

## 🧩 Extensibility

- **Pipeline behaviors:** implement `IRequestPipelineBehavior`, `IEventPipelineBehavior`, or the typed/stream variants.
- **Registration groups:** implement `IRegisterGroup` to modularize and share handler registration logic.
- **Outbox & commits:** implement `IOutboxStorage`, `IOutboxCommit` for transactional event persistence.

### Cross-assembly pipeline behaviors

An open-generic `[PipelineBehavior]` is applied to **every** request/event/stream handler the
declaring assembly can see — including handlers defined in **referenced** assemblies. The generator
emits one closed (Native-AOT-safe) registration per matching handler, so a behavior registered in
your composition root blankets the whole reference graph automatically. Constraints (e.g.
`where TRequest : ISecuredRequest`) still filter which handlers a behavior wraps.

This propagation flows **downward only** — along the reference direction. A behavior declared in a
library cannot wrap a handler in an application that references it (the library cannot see that
application). Place behaviors meant to apply everywhere in the composition root.

Event behaviors (`IEventPipelineBehavior<TEvent>`) and stream behaviors get the same treatment —
including `where TEvent : …` / `where TRequest : …` constraint filtering, so an open-generic event
behavior constrained to a marker interface only wraps events that implement it.

**CQRS boundary enforcement** follows the same downward propagation. Register the built-in behavior
globally once at the composition root:

```csharp
[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<>))]
[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<,>))]
```

That covers request handlers in referenced assemblies too — no need to repeat the attributes in every
sub-project. Leaving them on a referenced library is harmless: duplicate enforcement registrations are
deduplicated at the service-collection level (the behavior is not idempotent, so this dedup is what
keeps it safe).

To opt an assembly out and restrict its behaviors (and CQRS enforcement) to same-assembly handlers,
apply `[assembly: DisableSynapseCrossAssemblyBehaviors]`.

Handlers the generator cannot see — those registered manually at runtime via
`cfg.RegisterRequestHandler<…>()`, or living in an assembly the generator does not scan — are not
covered by the attributes. Enforce them explicitly with
`cfg.RegisterCqrsBoundaryEnforcement<TRequest>()` (or the `<TRequest, TResponse>` overload) in the
composition root. The registration is closed (Native-AOT safe) and deduplicated, so it is safe to
call even for a request the generator also covers.

> **Ordering caveat:** the `Order` property sorts behaviors only within a single `IRegisterGroup`.
> Across separately composed `RegisterGroup`s (e.g. one per assembly), pipeline position follows
> `AddRegisterGroup` call order. CQRS boundary enforcement is registered "first" and stays outermost
> regardless.

> Note: Transport/distributed messaging APIs are intentionally not documented here — they may change prior to the first
> stable release.

## 🤝 Contributing

We welcome contributions! Please read `CONTRIBUTING.md` for standards, development setup, and the PR process.

## 📝 Release notes

See releases on GitHub for detailed changelogs and version history: https://github.com/UnambitiousFx/Synapse/releases

## 📄 License

This project is licensed under the MIT License - see the `LICENSE` file for details.

---

Made with ❤️ by the UnambitiousFx team
