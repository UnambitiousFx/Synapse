# UnambitiousFx.Synapse — Guidelines

Lightweight, performance-oriented library for message-driven apps and in-process mediators. Composable primitives for Commands, Queries, Events, Pipelines with low latency/allocations.

## Core Principles

- **Simplicity**: Minimal, explicit APIs. Small single-purpose handlers, clear message contracts.
- **Performance**: Minimize allocations in hot paths. Avoid needless async; favor `ValueTask`.
- **Observability**: Carry correlation info; make tracing/logging/metrics easy in pipeline stages.
- **Extensibility**: Pluggable cross-cutting concerns (validation, retries, logging).
- **Reliability**: Idempotent handlers, explicit errors via `Result`/`OneOf` or typed errors — not exceptions.

## Code Style

- File-scoped namespaces (`namespace UnambitiousFx.Synapse;`).
- Naming: `PascalCase` public/types/methods, `camelCase` params/locals, `_camelCase` private fields, `IPascalCase` interfaces.
- Always braces, even single statements.
- XML doc comments (`<summary>`/`<param>`/`<returns>`) on public APIs.
- Prefer `ValueTask<T>` over `Task<T>` for likely-sync methods.
- Comments explain "why" not "what"; use sparingly.
- Don't expose internal implementation in public APIs.

## Messages & Handlers

- Distinguish Commands/Queries/Events by intent. Keep types small, serializable if crossing process boundaries.
- Handlers do one thing: validate, orchestrate domain calls, return typed result/failure.
- Pipelines for cross-cutting concerns; stages small and composable.
- Isolate side effects (IO, DB, external) and make injectable for testing.
- Prefer idempotent handlers; explicit retry in pipeline, not hidden behavior.

## Testing

AAA / Given-When-Then, separated by comments.

- **Arrange**: messages, mocks/stubs, configure stages.
- **Act**: invoke handler/pipeline/dispatcher.
- **Assert**: focused assertions on behavior + results.

```csharp
[Fact]
public void Handle_WithValidCommand_PerformsAction()
{
    // Arrange
    var command = new CreateThingCommand(...);
    var repo = Substitute.For<IThingRepository>();
    var handler = new CreateThingHandler(repo);

    // Act
    var result = handler.Handle(command);

    // Assert
    result.ShouldBe().Success();
    repo.Received(1).Add(Arg.Any<Thing>());
}
```

Rules:
- Names: `Method_Scenario_ExpectedBehavior`.
- `[Theory]`/`[InlineData]` for multiple scenarios.
- Cover nulls, invalid messages, boundaries, failure paths.
- `NSubstitute` for mocks; test handlers in isolation + integration tests for end-to-end dispatch.
- For `Result`/`Maybe` from `UnambitiousFx.Functional`, use `Functional.xunit` assertions.

## Performance Testing

Hot-path changes: add/update benchmarks in `benchmarks/SynapseBenchmark` (BenchmarkDotNet). Realistic scenarios, cold + warm runs.

## Integration Tests

Validate dispatcher↔pipelines↔handlers wiring. Lightweight test host or in-memory transports. Deterministic, behavior-focused.

## Changelog

Bug found **and** resolved on a branch → record in three files:

1. `docs/known-issues/NNN-slug.md` — detailed report (mirrors `.github/ISSUE_TEMPLATE/bug_report.yml` + `## Resolution`).
2. `docs/known-issues/README.md` — append index row.
3. `docs/docs/changelog.mdx` — append row to matching area section.

Use `/changelog` skill (severity/area/summary must match across files). Then `cd docs && pnpm build` to verify links.

## Contribution & Review

- Small, focused PRs.
- Add/update tests for behavioral changes; benchmarks for perf changes.
- Document non-obvious design decisions in PR description.
