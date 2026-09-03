# Synapse.Endpoints — Feature status

Feature inventory for `UnambitiousFx.Synapse.Endpoints`, scoped to what a usable REPR
(Request–Endpoint–Response) implementation needs.

Each ❌ / 🟡 row links to a file in [`features/`](features/) with the problem, the current state
(with source references), the code that cannot be written today and what the compiler, the analyzer
or the runtime answers instead, a proposed API with the same scenario written against it, and
acceptance criteria. Shipped features are listed here
only; their user-facing documentation lives under [`../docs/endpoints/`](../docs/endpoints/).

**Legend** — ✅ Shipped · 🟡 Partial · ❌ Missing

> The package is preview-only and its public surface is not frozen. See
> [Preview status](../docs/endpoints/reference/preview.mdx).

## Shipped

| Feature | Status | Description |
|---|---|---|
| Endpoint per class | ✅ | Five tiers: `RawEndpoint`, `RawEndpoint<TRequest[, TResponse]>`, `Endpoint<TRequest[, TResponse]>`, `MappedEndpoint<…>`, `StreamEndpoint<TRequest, TItem>`. Each tier adds one thing to the one below it. |
| Compile-time request binding | ✅ | One `IEndpointBinder<T>` generated per message type, assigning properties directly. No reflection at request time. |
| Binding sources: route, query, header, body | ✅ | Five resolution rules; `[FromRoute]`/`[FromQuery]`/`[FromBody]` (MVC) and `[FromHeader]` (MVC or Synapse's own) pin a source explicitly. |
| Error accumulation | ✅ | `BindingValidator` collects every presence/parse failure into one `400` `HttpValidationProblemDetails`. Allocates nothing on the valid path. |
| Declarative responses | ✅ | `Ok` / `Created` / `Accepted` / `NoContent` / `StatusCode` on the builder, or an `OnSuccess` override. |
| Failure mapping | ✅ | Dispatch failures flow through the registered `IFailureHttpMapper` unchanged. |
| Endpoint groups | ✅ | `[InGroup<TGroup>]` plus a group `Configure` contributing prefix, tags and authorization. One `MapGroup` per group, cached. |
| Authorization | ✅ | `RequireAuthorization(params string[])` at endpoint and group level; `AllowAnonymous()` at endpoint level only, so an endpoint can opt out of its group's policy. |
| Streaming | ✅ | `StreamEndpoint<TRequest, TItem>` negotiating SSE vs an incrementally written JSON array on the `Accept` header. |
| Separate wire contract | ✅ | `MappedEndpoint<THttpRequest, TRequest, TResponse, THttpResponse>` when the HTTP shape must evolve independently of the message. |
| OpenAPI: request and success response | ✅ | `Accepts` declared only when a body is actually read; success status and body type declared from the resolved configuration. |
| OpenAPI: declared failure responses | ✅ | `Produces` / `Produces<TBody>` / `ProducesProblem` / `ProducesValidationProblem` on every endpoint builder, so the statuses the `IFailureHttpMapper` emits can be documented. A bodyless status is declared as `void`, not skipped. |
| Native AOT | ✅ | No reflection on any request path; `SYNE008` checks `JsonSerializerContext` registration at compile time. |
| Analyzer diagnostics | ✅ | `SYNE001`–`SYNE015` covering route/property mismatches, binding conflicts, unassignable or unparsable properties, and success-mapping mistakes. |
| Duplicate-route detection | ✅ | Startup check over Synapse-marked endpoints only, after group prefixes are applied. |
| Optional readers for reference types | ✅ | Each `…Optional<T>` on `BindingValidator` is a `struct`- and a `class`-constrained overload pair, so `QueryOptional<string>` and `QueryOptional<CallbackUrl>` go through the collector. Mirrored as `TryGet…Optional<T>` on `HttpContextBindingExtensions`. |
| Escape hatches | ✅ | `Raw(Action<RouteHandlerBuilder>)` on every endpoint builder and `Raw(Action<RouteGroupBuilder>)` on groups. |
| Endpoint test harness | ✅ | `EndpointHarness.Create<TEndpoint>()` maps one endpoint through the real routing stack — real constraints, real group prefixes, real `405` — with no host. Only `IInvoker` is faked, so the failure mapper stays under test. Ships as `UnambitiousFx.Synapse.Endpoints.Testing`. |
| Lifecycle hooks | ✅ | `OnBeforeHandleAsync` / `OnAfterHandleAsync` / `OnBindFailedAsync` on every bound tier, plus `PreProcessor<T>()` / `PostProcessor<T>()` resolved from `HttpContext.RequestServices`. The exit steps run on the bind-failure path too, so a response-header processor does not skip `400`s. |

## Gaps

Ordered by recommended implementation sequence.

| # | Feature | Status | Priority | Description |
|---|---|---|---|---|
| [005](features/005-self-handled-endpoint-tier.md) | Self-handled endpoint tier | ❌ | High | Every bound tier requires `TRequest : IRequest<…>` and dispatches through the mediator. No "bind, run this, return that" tier — the classic REPR shape. |
| [006](features/006-form-and-file-binding.md) | Form, multipart and file binding | ❌ | Med-High | No `IFormFile`, no form binding, no multipart. File uploads must drop to `RawEndpoint`. |
| [007](features/007-collection-binding.md) | Collection binding | ❌ | Med-High | `?tag=a&tag=b` cannot bind to `string[]`; the property is rejected by `SYNE012` as unparsable. |
| [008](features/008-additional-binding-sources.md) | Claims, cookies and services as sources | ❌ | Medium | Only four sources exist. No `[FromClaim]` — so the ergonomic path to a caller id is a client-controlled header. |
| [009](features/009-custom-value-parsers.md) | Custom value parsers | ❌ | Medium | A bound type must be `string`, an enum, or expose `TryParse`. `SYNE012`'s advice is unusable for a type you do not own. |
| [010](features/010-endpoint-validators.md) | Per-endpoint validators | 🟡 | Medium | Business validation works but lives in DI configuration, and its error shape differs from the binding path's field-keyed `400`. |
| [011](features/011-multiple-routes-and-verbs.md) | Multiple routes and verbs | ❌ | Medium | One verb, one route per class. `EndpointBuilderCore.Route` overwrites rather than accumulates; route attributes are `AllowMultiple = false`. |
| [012](features/012-nested-groups.md) | Nested groups, richer group metadata | 🟡 | Medium | Groups are one level deep and carry prefix/tags/auth only. No `/api` → `/api/v1` → `/api/v1/tasks`. |
| [013](features/013-global-conventions.md) | Global endpoint conventions | ❌ | Medium | `MapSynapseEndpoints` takes no configuration callback, so an app-wide prefix, policy, tag or filter must be repeated per endpoint. |
| [014](features/014-typed-result-unions.md) | Typed result unions | ❌ | Medium | One declared success status per endpoint. `200`-or-`201` upserts and `200`-or-`304` conditional reads cannot be described. |
| [015](features/015-api-versioning.md) | API versioning | ❌ | Medium | Not merely unsupported: the documented `Raw` + matcher-policy workaround trips the startup duplicate-route check and fails to boot. |
| [016](features/016-link-generation.md) | Link generation for `Location` | 🟡 | Low | `Created` takes a URL-building delegate, so route templates are duplicated as interpolated strings. `Name()` exists but `CreatedAtRoute` does not. |
| [017](features/017-endpoint-dependency-injection.md) | Dependency injection into endpoints | 🟡 | Low | Singleton endpoints, `context.Service<T>()` service location. Coherent by design; listed for completeness and ranked last. |

## Suggested sequencing

1. **005–006** close the ergonomic gap with established REPR implementations.
2. **007–015** round out binding, routing and grouping.
3. **016–017** are polish, and 017 may reasonably be closed as "working as intended".

001–004 are shipped: the OpenAPI document no longer lies about what an endpoint can return, the most
common optional parameter is expressible on the collector, an endpoint can be exercised without a
host, and an endpoint has a seam on both sides of dispatch.
