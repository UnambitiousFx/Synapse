# Guidelines for UnambitiousFx.Synapse

This document provides guidelines and best practices for developing and maintaining the UnambitiousFx.Synapse library.

## Project Overview

UnambitiousFx.Synapse is a lightweight, performance-oriented library for building message-driven applications and in-process mediators. Its core goal is to provide composable primitives for Commands, Queries, Events, and Pipelines while keeping latency and allocations low.

### Core Principles

- **Simplicity**: Keep APIs minimal and explicit. Prefer small, single-purpose handlers and clear contracts for messages.
- **Performance**: Minimize allocations in hot paths and avoid unnecessary async/await where synchronous code suffices; favor `ValueTask` where appropriate.
- **Observability**: Ensure messages carry correlation information when needed and make it easy to add tracing, logging, and metrics in pipeline stages.
- **Extensibility**: Design pipeline and handler abstractions so cross-cutting concerns (validation, retries, logging) are pluggable.
- **Reliability**: Favor idempotent handlers and explicit error handling; surface errors through `Result`/`OneOf` or typed error objects rather than plain exceptions.

## Code Style and Conventions

- **Namespaces**: Use file-scoped namespaces (e.g., `namespace UnambitiousFx.Synapse;`).
- **Naming**:
  - `PascalCase` for public members, types, and methods.
  - `camelCase` for parameters and local variables.
  - `_camelCase` for private fields.
  - `IPascalCase` for interfaces.
- **Braces**: Always use braces for control flow, even for single statements.
- **Documentation**: Public APIs must include XML documentation comments (`<summary>`, `<param>`, `<returns>`, etc.).
- **Async**: Prefer `ValueTask<T>` over `Task<T>` for methods that are likely to complete synchronously; avoid unnecessary allocations in hot paths.

## Message & Handler Guidelines

- **Message Types**: Distinguish Commands, Queries, and Events by intent. Keep message types small and serializable if they may cross process boundaries.
- **Handlers**: Keep handlers focused. A handler should do one thing: validate, orchestrate domain calls, and return a typed result or failure.
- **Pipelines**: Use pipelines for cross-cutting concerns. Keep pipeline stages small and composable.
- **Side Effects**: Isolate side effects (IO, DB, external calls) and make them injectable for testing.
- **Idempotency**: Design handlers to be idempotent when appropriate; prefer explicit retry semantics in the pipeline rather than hidden behavior.

## Unit Testing Guidelines

Testing is important. Aim for high coverage and clear, readable tests.

### AAA Pattern (Gherkin style)

Tests should follow the Arrange-Act-Assert pattern (Given-When-Then). Use comments to separate these sections for clarity.

- **Arrange (Given)**: Create message objects, mocks/stubs for dependencies, and configure pipeline stages if necessary.
- **Act (When)**: Invoke the handler, pipeline, or dispatcher under test.
- **Assert (Then)**: Verify behavior and returned results with clear, focused assertions.

#### Example

```csharp
[Fact]
public void Handle_WithValidCommand_PerformsAction()
{
    // Arrange (Given)
    var command = new CreateThingCommand(...);
    var repo = Substitute.For<IThingRepository>();
    var handler = new CreateThingHandler(repo);

    // Act (When)
    var result = handler.Handle(command);

    // Assert (Then)
    result.ShouldBe().Success();
    repo.Received(1).Add(Arg.Any<Thing>());
}
```

### Best Practices

1. **Descriptive Test Names**: Use `Method_Scenario_ExpectedBehavior`.
2. **Theory Tests**: Use `[Theory]` and `[InlineData]` to validate multiple scenarios.
3. **Edge Cases**: Always test nulls, invalid messages, boundary conditions, and failure paths.
4. **Mocks**: Use `NSubstitute` for mocking external dependencies; prefer testing handlers in isolation and add integration tests for end-to-end dispatch behavior.
5. **Functional Types**: If using `Result`/`Maybe` from `UnambitiousFx.Functional`, use `Functional.xunit` assertions for clarity when available.

## Performance Testing

For changes affecting hot paths, add or update benchmarks in `benchmarks/SynapseBenchmark` using BenchmarkDotNet. Benchmark scenarios should be realistic and include cold and warm runs.

## Integration Tests

- Validate wiring between the dispatcher, pipelines, and handlers.
- Use a lightweight test host or in-memory transports where appropriate.
- Keep integration tests deterministic and focused on behavior rather than implementation details.

## Contribution & Review

- Keep PRs small and focused.
- Add or update tests for behavioral changes.
- Add benchmarks for performance-relevant changes.
- Document non-obvious design decisions in PR descriptions.

## Miscellaneous

- Avoid exposing internal implementation details in public APIs.
- Use code comments sparingly to explain "why" rather than "what".

---

These guidelines are intended to be lightweight and pragmatic—adapt as necessary for the problem domain while preserving the core principles above.
