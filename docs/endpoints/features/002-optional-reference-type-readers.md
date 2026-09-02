# 002 — Optional readers for reference types

|  |  |
|---|---|
| **Status** | ✅ Shipped |
| **Priority** | High |
| **Area** | Binding |
| **Tiers** | Low level (`BindingValidator`, `HttpContextBindingExtensions`), generated binders |
| **Breaking** | No — additive |

## Problem

`BindingValidator` cannot read an optional `string` query parameter — or an optional value of any
reference type. `?search=` is about the most common optional input an HTTP API has, and the
collector that every hand-written `BindAsync` is pointed at cannot express it.

## State before this change

`src/Synapse.Endpoints/Binding/BindingValidator.cs`:

```csharp
public bool RouteOptional<T>(string name, out T? value) where T : struct, IParsable<T>
public bool QueryOptional<T>(string name, out T? value) where T : struct, IParsable<T>
public bool HeaderOptional<T>(string name, out T? value) where T : struct, IParsable<T>
```

The `struct` constraint exists so `out T?` means `Nullable<T>`. It excludes `string` and every other
reference type outright. `HttpContextBindingExtensions` has the same shape — its typed readers are
all `where T : IParsable<T>` with a required-style `bool` return and no optional variant at all,
apart from the untyped `Header(name)` helper that returns `string?`.

The workaround was `context.TryGetQuery("search", out var raw)` and ignoring the result, which
bypasses the validator entirely and so contributes nothing to the accumulated `400`.

## What you could not write

A hand-written binder for `GET /reports?page=2&search=ship&callback=https://acme.test/done`, where
`search` and `callback` are both optional:

```csharp
var v = context.Validate();

v.QueryOptional<int>("page", out var page);            // fine — int is a struct

v.QueryOptional<string>("search", out var search);
// CS0453: The type 'string' must be a non-nullable value type in order to use it as
//         parameter 'T' in the generic type or method 'BindingValidator.QueryOptional<T>(…)'

v.QueryOptional<CallbackUrl>("callback", out var callback);
// CS0453 again — CallbackUrl is a class, even though it implements IParsable<CallbackUrl>
```

So the optional reads left the collector, and one `BindAsync` ended up written in two styles:

```csharp
var v = context.Validate();
v.QueryOptional<int>("page", out var page);

// Off the collector from here down.
context.TryGetQuery("search", out var search);

// Absent and unparsable collapse into the same answer: a caller who sends ?callback=not-a-url
// gets a null callback and a 200. Nothing was added to the accumulated 400, because the
// validator never saw the value.
CallbackUrl? callback = context.TryGetQuery("callback", out var rawCallback)
                     && CallbackUrl.TryParse(rawCallback, CultureInfo.InvariantCulture, out var parsed)
    ? parsed
    : null;
```

Scope note: the generated binders were not affected. `BinderEmitter.EmitValueRead` emits a
`BindingHelpers.TryGetQuery` call plus a presence flag for a nullable property rather than calling
`QueryOptional<T>`, so `string? Title` on a message bound correctly already —
`SearchTasksQuery.Title` in `examples/EndpointsApi` is exactly that shape. The gap was the
hand-written tier, and the asymmetry was itself the defect: the docs sell the collector as *"the same
collector the high level's generated binders use"*, and it could not express what the generated binder
emits.

## Shipped API

An overload pair, not the separately-named members this document originally proposed:

```csharp
// BindingValidator — the struct-constrained members are unchanged; each gains a sibling.
public bool RouteOptional<T>(string name, out T? value)  where T : class, IParsable<T>;
public bool QueryOptional<T>(string name, out T? value)  where T : class, IParsable<T>;
public bool HeaderOptional<T>(string name, out T? value) where T : class, IParsable<T>;
```

The original proposal assumed a class-constrained overload would be a duplicate signature — the same
reason the enum readers are named `…Enum` — and so proposed `QueryOptionalRef<T>` alongside a
non-generic `QueryOptionalString`. That assumption is wrong for *these* members: the `out` parameter
differs as well as the constraint. `T?` is `Nullable<T>` under `struct` and plain `T` under `class`,
so the two signatures are `Nullable<T>&` and `T&` and the compiler accepts both. The enum readers do
not escape the same way, because their parameter is `T` either way. Verified compiling on
`net8.0`, `net9.0` and `net10.0`.

So there is one name per source, six fewer public members than proposed, and `string` needs no
special case:

```csharp
var v = context.Validate();

v.QueryOptional<int>("page", out var page);            // int?         — unchanged
v.QueryOptional<string>("search", out var search);     // string?      — absent → null, never an error
v.QueryOptional<CallbackUrl>("callback", out var cb);  // CallbackUrl? — absent → null, bad → reported

if (!v.IsValid)
{
    return ValueTask.FromResult(BindResult<ReportQuery>.Failure(v));
}
```

```json title="GET /reports?page=2&callback=not-a-url"
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "callback": ["The query value is not a valid CallbackUrl."] }
}
```

`?search=` absent is still a `200`; `?callback=` present and wrong is now a `400` that names the
field, alongside every other problem the same request had.

### Mirrored on `HttpContextBindingExtensions`

The extensions tier had no optional reader at all, for value or reference types — its typed readers
answer `false` to an absent value and an invalid one alike. It gained the same pair, under the
tier's `TryGet…` naming:

```csharp
public static bool TryGetRouteOptional<T>(this HttpContext context, string name, out T? value);
public static bool TryGetQueryOptional<T>(this HttpContext context, string name, out T? value);
public static bool TryGetHeaderOptional<T>(this HttpContext context, string name, out T? value);
// each with a struct-constrained and a class-constrained overload
```

The contract is the validator's, minus the error collection: absent is `true` with a `null` value,
and only present-but-unparsable is `false`. Adding only the reference-typed half would have left
`TryGetQueryOptional<string>` compiling while `TryGetQueryOptional<int>` did not — this document's
own complaint, inverted.

## Acceptance criteria

- [x] An absent optional value reports nothing and yields `null`.
- [x] A present-but-unparsable optional value reports a parse error and returns `false`, matching
      the struct overloads.
- [x] Generated binders keep binding nullable reference-typed properties correctly — `EmitValueRead`
      was left alone, and is now pinned by a regression guard.
- [x] A nullable `string?` property on a message still produces no "is required" error
      (regression guard for `SearchTasksQuery.Title`).
- [x] Tests in `test/Synapse.Endpoints.Tests/BindingValidatorTests.cs` and
      `HttpContextBindingExtensionsTests.cs`.

## Notes

What the generator emits for `string?` was checked before writing this up: `BinderEmitter.EmitValueRead`
takes the nullable branch, emits a `TryGetQuery` call plus a presence flag, and reports nothing when
the value is absent. So this was a missing convenience at the hand-written tier, not a correctness bug
in the generated one — which is why two of the acceptance criteria are regression guards rather than
fixes.

This also retires the advice in `docs/known-issues/053`, which concluded that an optional string does
not belong on the collector because it cannot fail. That reasoning holds for `string` alone and never
covered `CallbackUrl` — a reference type that parses, and therefore can fail — which is the case that
made the constraint indefensible.

Verification: `BindingValidatorTests` covers all three sources for a reference type plus the
absent/present/invalid split; `HttpContextBindingExtensionsTests` covers the same at the extensions
tier; `BinderEmissionEdgeCaseTests.Generate_ForNullableStringQueryValue_ReportsNothingWhenItIsAbsent`
asserts the generated binder still reads `string? Title` behind a presence flag and reports nothing
for it.
