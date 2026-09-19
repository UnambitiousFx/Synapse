# 001 — Declare failure responses in OpenAPI

|  |  |
|---|---|
| **Status** | ✅ Shipped |
| **Priority** | High |
| **Area** | OpenAPI / Builders |
| **Tiers** | `Endpoint<…>`, `BoundEndpoint<…>`, `ContractEndpoint<…>`, `StreamEndpoint<…>` |
| **Breaking** | No — additive |

## Problem

An endpoint's OpenAPI document describes only two outcomes: the configured success status and a
`400` validation problem. Every other status the endpoint really produces — the `404`, `409`, `401`
and `500` that `IFailureHttpMapper` writes when the dispatched message returns a `Result` failure —
is absent from the document. Generated clients therefore have no error model at all.

The high-level builder also has no way to add one by hand.

## State before this change

- `src/Synapse.Endpoints/RawEndpoint.Generic.cs` — `ApplyMetadata` emits exactly one
  `ProducesResponseMetadata` for the success status plus `handlerBuilder.ProducesValidationProblem()`.
  Identical code in `RawEndpoint.Void.cs`, `ContractEndpoint.cs` and `StreamEndpoint.cs`.
- `src/Synapse.Endpoints/Builders/IEndpointBuilder.cs` and `IEndpointBuilder.Generic.cs` expose no
  `Produces` / `ProducesProblem`. Only `IRawEndpointBuilder` has them
  (`src/Synapse.Endpoints/Builders/IRawEndpointBuilder.cs`), which is backwards: the low tier can
  describe itself, the high tier cannot.
- `IStreamEndpointBuilder` has none either.
- The escape hatch is `Raw(b => b.ProducesProblem(404))`, which the docs point at for filters and
  rate limiting, not for describing an endpoint's own contract.

## Shipped API

Added to `IEndpointBuilder`, `IEndpointBuilder<TResponse>` and `IStreamEndpointBuilder`, each
returning its own builder type so the fluent chain does not widen:

```csharp
IEndpointBuilder<TResponse> Produces(int statusCode);
IEndpointBuilder<TResponse> Produces<TBody>(int statusCode, string contentType = "application/json")
    where TBody : notnull;
IEndpointBuilder<TResponse> ProducesProblem(int statusCode);
IEndpointBuilder<TResponse> ProducesValidationProblem(int statusCode = 400);
```

`IRawEndpointBuilder` already had the two `Produces` overloads and gained the two problem
shorthands, so all four are now spelled the same way at every tier.

The bodyless overload is backed by `ProducesResponseMetadata` (which declares `void` rather than
`null`, so the entry is not skipped by `Microsoft.AspNetCore.OpenApi` — see
`docs/known-issues/051`); the two problem overloads delegate to the framework's own extensions, so a
declared `404` is structurally identical to the `400` each tier declares for itself.

All four live on `EndpointBuilderCore`, which every builder already delegates to, so the three public
surfaces cannot drift apart — and `RawEndpointBuilder`'s pre-existing `Produces` overloads were moved
onto the same code path.

## Acceptance criteria

- [x] `Produces` / `ProducesProblem` available on every builder interface, returning the correctly
      typed builder so the fluent chain does not widen.
- [x] Declared statuses appear in the generated OpenAPI document for all five endpoint tiers.
- [x] A declared status with no body is emitted as `void`, not skipped (regression guard for the
      behaviour `ProducesResponseMetadata` already documents).
- [x] Tests in `test/Synapse.Endpoints.Tests/OpenApiMetadataTests.cs`.

## Not shipped

`ProducesMappedFailures()` — the optional convention above — was left out deliberately. It would have
to hardcode a status table belonging to `UnambitiousFx.Functional.AspNetCore`'s
`DefaultFailureHttpMapper`: `IFailureHttpMapper` exposes only `GetFailureResponse`, not the statuses
it can produce, and the mapper is replaceable through DI. A guessed table would go stale silently and
be wrong outright for a custom mapper — which is the same class of defect this feature exists to fix,
just moved one level up. It is worth revisiting if `IFailureHttpMapper` ever publishes its own table.

## Notes

Cheapest fix in the backlog and the one with the widest reach: before this, the published document was
not merely incomplete, it was wrong about what the endpoint can return.

Verification: `Produces`/`ProducesProblem` per tier is pinned in
`test/Synapse.Endpoints.Tests/OpenApiMetadataTests.cs`; that the declarations reach the served
document is pinned end-to-end against `/openapi/v1.json` in
`examples/EndpointsApi.Tests/TaskEndpointsTests.cs`, because metadata presence is not document
presence — that distinction is exactly how the bodyless declarations of `docs/known-issues/051` went
missing.
