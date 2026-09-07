# Synapse.Endpoints — Feature status

Feature inventory for `UnambitiousFx.Synapse.Endpoints`, scoped to what a usable REPR
(Request–Endpoint–Response) implementation needs.

Each ❌ / 🟡 row links to a file in [`features/`](features/) with the problem, the current state
(with source references), the code that cannot be written today and what the compiler, the analyzer
or the runtime answers instead, a proposed API with the same scenario written against it, and
acceptance criteria. Shipped features are listed here
only; their user-facing documentation lives under [`../docs/endpoints/`](../docs/endpoints/).

Every Medium-priority gap below now has a design spec under
[`../superpowers/specs/`](../superpowers/specs/), linked from its row. A feature doc states the
problem; its spec resolves the open API choices, names the diagnostics, and sequences the work.
Where the two disagree, **the spec is current** — three feature docs rest on claims the code has
since outgrown, and each spec says so explicitly with evidence.

**Legend** — ✅ Shipped · 🟡 Partial · ❌ Missing

> The package is preview-only and its public surface is not frozen. See
> [Preview status](../docs/endpoints/reference/preview.mdx).

## Shipped

| Feature | Status | Description |
|---|---|---|
| Endpoint per class | ✅ | Five tiers: `RawEndpoint`, `RawEndpoint<TRequest[, TResponse]>`, `Endpoint<TRequest[, TResponse]>`, `MappedEndpoint<…>`, `StreamEndpoint<TRequest, TItem>`. Each tier adds one thing to the one below it. |
| Compile-time request binding | ✅ | `BindAsync` generated per endpoint, as an `override` on the endpoint's own `partial` class, assigning properties directly. No reflection at request time, and no shared lookup by message type — each endpoint's binding is resolved from its own route and verb. |
| Binding sources: route, query, header, form, body | ✅ | Six resolution rules; `[FromRoute]`/`[FromQuery]`/`[FromForm]`/`[FromBody]` (MVC) and `[FromHeader]` (MVC or Synapse's own) pin a source explicitly. |
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
| Analyzer diagnostics | ✅ | `SYNE001`–`SYNE021` covering route/property mismatches, binding conflicts, unassignable, unparsable or unsupported-collection properties, success-mapping mistakes, form-binding conflicts or inference, and the shape rules a generated binding depends on (`partial`, no hand-written `BindAsync` on a generated tier). |
| Duplicate-route detection | ✅ | Startup check over Synapse-marked endpoints only, after group prefixes are applied. |
| Optional readers for reference types | ✅ | Each `…Optional<T>` on `BindingValidator` is a `struct`- and a `class`-constrained overload pair, so `QueryOptional<string>` and `QueryOptional<CallbackUrl>` go through the collector. Mirrored as `TryGet…Optional<T>` on `HttpContextBindingExtensions`. |
| Escape hatches | ✅ | `Raw(Action<RouteHandlerBuilder>)` on every endpoint builder and `Raw(Action<RouteGroupBuilder>)` on groups. |
| Endpoint test harness | ✅ | `EndpointHarness.Create<TEndpoint>()` maps one endpoint through the real routing stack — real constraints, real group prefixes, real `405` — with no host. Only `IInvoker` is faked, so the failure mapper stays under test. Ships as `UnambitiousFx.Synapse.Endpoints.Testing`. |
| Lifecycle hooks | ✅ | `OnBeforeHandleAsync` / `OnAfterHandleAsync` / `OnBindFailedAsync` on every bound tier, plus `PreProcessor<T>()` / `PostProcessor<T>()` resolved from `HttpContext.RequestServices`. The exit steps run on the bind-failure path too, so a response-header processor does not skip `400`s. |
| Self-handled endpoint tier | ✅ | `SelfHandledEndpoint<TRequest, TResponse>` and `SelfHandledEndpoint<TRequest>`: the generated binder, the declarative responses and the lifecycle hooks of the high level, with an `ExecuteAsync` returning `Result<T>` in place of dispatch. `TRequest` needs no `IRequest<…>`, and failures still go through the registered `IFailureHttpMapper`. No pipeline behaviour wraps it. |
| Collection binding | ✅ | `?tag=a&tag=b&tag=c` binds a `T[]`, `List<T>`, `IReadOnlyList<T>` or `IEnumerable<T>` property from a repeated query key, header or form field — see [007](features/007-collection-binding.md) and [Repeated keys](../docs/endpoints/high-level/messages.mdx#repeated-keys). |
| Form, multipart and file binding | ✅ | `[FromForm]`, or a bare `IFormFile`/`IFormFileCollection`/collection-of-`IFormFile` property, binds a message from `multipart/form-data` or `application/x-www-form-urlencoded` — the whole message, not just that property, since a request is a form or JSON, never both. Its OpenAPI schema needs the [018](features/018-openapi-parameter-metadata.md) satellite package; the core package alone declares both content types with no schema. See [006](features/006-form-and-file-binding.md) and [Forms and files](../docs/endpoints/high-level/messages.mdx#forms-and-files). |
| OpenAPI parameter metadata | ✅ | Query, header and route-bound properties are declared as `operation.parameters` — scalar or array, with the binder's own name and `required` rule — and a form-bound message's request body gets a real field-by-field schema, both via the opt-in `UnambitiousFx.Synapse.Endpoints.OpenApi` package (`net10.0` only; the core package stays free of any OpenAPI dependency). See [018](features/018-openapi-parameter-metadata.md) and [OpenAPI](../docs/endpoints/reference/openapi.mdx#declaring-query-header-and-route-parameters). |

## Gaps

Ordered by recommended implementation sequence. **Spec** links the design document that resolves the
feature's open questions.

| # | Feature | Status | Priority | Spec | Description |
|---|---|---|---|---|---|
| [008](features/008-additional-binding-sources.md) | Claims and cookies as sources | ❌ | Medium | [spec](../superpowers/specs/2026-09-05-additional-binding-sources-design.md) | Only five sources exist. No `[FromClaim]` — so the ergonomic path to a caller id is a client-controlled header. `[FromServices]` is out of scope here; see 017. |
| [009](features/009-custom-value-parsers.md) | Custom value parsers | ❌ | Medium | [spec](../superpowers/specs/2026-09-05-custom-value-parsers-design.md) | A bound type must be `string`, an enum, or expose `TryParse`. `SYNE012`'s advice is unusable for a type you do not own. |
| [010](features/010-endpoint-validators.md) | Per-endpoint validators | 🟡 | Medium | [spec](../superpowers/specs/2026-09-05-endpoint-validators-design.md) | Binding failures are field-keyed; business-rule failures are an opaque problem document. One endpoint, two error models. **Colocation is already solved** by `[Validator]`, and `known-issues/004` is resolved — the spec narrows this to the error contract and the status. Blocked on a `UnambitiousFx.Functional` release. |
| [011](features/011-multiple-routes-and-verbs.md) | Multiple routes and verbs | ❌ | Medium | [spec](../superpowers/specs/2026-09-05-routes-verbs-and-versioning-design.md) | One verb, one route per class. `EndpointBuilderCore.Route` overwrites rather than accumulates; route attributes are `AllowMultiple = false`. **Breaking**, contrary to the feature doc's header: `MapEndpoint<T>` cannot keep returning one `RouteHandlerBuilder`. |
| [015](features/015-api-versioning.md) | API versioning | ❌ | Medium | [spec](../superpowers/specs/2026-09-05-routes-verbs-and-versioning-design.md) | Not merely unsupported: the documented `Raw` + matcher-policy workaround trips the startup duplicate-route check and fails to boot. Specced with 011, in two phases — the opt-out ships alone. |
| [012](features/012-nested-groups.md) | Nested groups, richer group metadata | 🟡 | Medium | [spec](../superpowers/specs/2026-09-05-group-nesting-and-conventions-design.md) | Groups are one level deep and carry prefix/tags/auth only. No `/api` → `/api/v1` → `/api/v1/tasks`. |
| [013](features/013-global-conventions.md) | Global endpoint conventions | ❌ | Medium | [spec](../superpowers/specs/2026-09-05-group-nesting-and-conventions-design.md) | `MapSynapseEndpoints` takes no configuration callback, so an app-wide prefix, policy, tag or filter must be repeated per endpoint. |
| [014](features/014-typed-result-unions.md) | Typed result unions | ❌ | Medium | [spec](../superpowers/specs/2026-09-05-typed-result-unions-design.md) | One declared success status per endpoint. `200`-or-`201` upserts and `200`-or-`304` conditional reads cannot be described. |
| [016](features/016-link-generation.md) | Link generation for `Location` | 🟡 | Low | — | `Created` takes a URL-building delegate, so route templates are duplicated as interpolated strings. `Name()` exists but `CreatedAtRoute` does not. |
| [017](features/017-endpoint-dependency-injection.md) | Dependency injection into endpoints | 🟡 | Low | — | Singleton endpoints, `context.Service<T>()` service location. Coherent by design; listed for completeness and ranked last. |

## Suggested sequencing

1. **018 is shipped**, closing the OpenAPI gap 006 and 007 both left behind — neither could describe
   a parameter or a multipart schema on its own, and both said so in their own acceptance criteria —
   and building the parameter pipeline 008 plugs into next. The original ordering put 018 last, which
   could not have worked: two of 008's acceptance criteria are about the document.
2. **008–009** round out binding on top of it.
3. **010** is blocked on an external release and its remaining scope is smaller than its feature doc
   suggests. Its *documentation* corrections are not blocked and should be done immediately.
4. **011 + 015** together rewrite `ThrowOnDuplicateRoutes` and multi-map an endpoint. 015 phase 1
   (the `AllowDuplicateRoute()` opt-out) is small, unblocks users today, and should ship on its own
   even if first-class versioning is deferred.
5. **012 + 013** together compose prefixes and policy at group and application scope.
6. **014** is self-contained and can be taken at any point after 001, which is shipped.
7. **016–017** are polish, and 017 may reasonably be closed as "working as intended".

Diagnostic IDs are pre-allocated across the specs so implementation order does not matter — next
free is `SYNE033`:

| Spec | IDs |
|---|---|
| 008 | `SYNE031`–`SYNE032` |
| 009 | `SYNE022` |
| 010 | `SYNE023` |
| 011 + 015 | `SYNE024`–`SYNE026` |
| 012 + 013 | `SYNE027`–`SYNE028` |
| 014 | `SYNE029`–`SYNE030` |

`SYNE020`–`SYNE021` were taken by the endpoint-partial-generation work (`EndpointMustBePartial` and
`BindAsyncIsGenerated`), so 008 was moved off them rather than left to collide; 009–014 keep the IDs
they were given, because moving them would churn a planning document for nothing.

**`SYNE013` is retired and must not be reused.** It was the shared-binder conflict rule, deleted when
each endpoint got its own generated binding. A consumer still carrying a `NoWarn` for it would
silently suppress whatever unrelated rule inherited the number.

Keep this table current when a rule ships: it claimed `SYNE019` was free while `InferredFormBinding`
already held it, and the endpoint-partial-generation plan had to be corrected mid-flight as a result.

001–007 and 018 are shipped: the OpenAPI document no longer lies about what an endpoint can return, the most
common optional parameter is expressible on the collector, an endpoint can be exercised without a
host, an endpoint has a seam on both sides of dispatch, a route with no domain message behind it no
longer has to invent one, a form or multipart request binds straight into a message — a file
included — with the same error accumulation and declarative responses every other endpoint gets, a
repeated query key, header or form field binds directly to a collection-shaped property instead of
being rejected as unparsable, and — with the opt-in OpenAPI satellite package — a query, header or
route parameter and a form message's field-by-field schema are both declared in the document instead
of being invisible or empty.
