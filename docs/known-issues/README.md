# Known Issues

Issues documented here were discovered during development and testing of the library.
Each file mirrors the [bug report template](.github/ISSUE_TEMPLATE/bug_report.yml) so it can be
filed as a GitHub issue with minimal editing.

| File | Summary | Severity | Area |
|---|---|---|---|
| [001](001-open-generic-pipeline-behavior-aot-value-type.md) | Open-generic pipeline behavior registrations throw at runtime under Native AOT when `TResponse` is a value type | **High** | Core DI |
| [002](002-validateonbuild-does-not-suppress-aot-open-generic-check.md) | `ValidateOnBuild = false` does not suppress the issue-001 runtime error | Medium | Core DI / Docs |
| [003](003-authorization-failure-maps-to-http-500.md) | Pipeline short-circuit via `Result.Failure<T>()` is mapped to HTTP 500 instead of 403/401 | Medium | AspNetCore mapping |

> **Discovery context:** all three were found while building the pipeline-behavior showcase in
> `examples/MinimalApi` on branch `feature/typed-pipeline-behaviors` against .NET 10 with
> `PublishAot=true`.
