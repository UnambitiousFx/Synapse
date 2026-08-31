# [Bug]: The documented validator sample does not compile

**Severity:** Low
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** Both the validator sample in `docs/docs/endpoints.mdx` and the `<example>` block on
> `BindingValidator` itself contained `v.QueryOptional<string>("sort", out var sort)`. The
> `…Optional<T>` members are constrained `where T : struct, IParsable<T>`, so that line is **CS0453**:
> the single most obvious optional query parameter, a string, cannot go through the collector at all.
> Both samples were wrong from the moment they were written and nothing compiled them.

---

## Describe the bug

`BindingValidator.QueryOptional<T>` — and `RouteOptional<T>` and `HeaderOptional<T>` — are declared
`where T : struct, IParsable<T>`, because "absent" is expressed as `T?` meaning `Nullable<T>`. A
reference type does not satisfy that constraint.

The published sample asked for `QueryOptional<string>`. Anyone copying it, from the documentation site
or from IntelliSense on the type itself, gets:

```
error CS0453: The type 'string' must be a non-nullable value type in order to use it as parameter 'T'
in the generic type or method 'BindingValidator.QueryOptional<T>(string, out T?)'
```

This is a documentation defect rather than a runtime one — the compiler catches it immediately — but it
was the first line of the first example a reader of the low level would try, and it pointed at an API
that does not exist.

Behind it sits a real asymmetry that the documentation never stated: **an optional string cannot fail.**
Absent means `null`, present means the string, and there is no parse step in between. The collector
exists to gather failures, so there is nothing for it to do and no member for it to offer. The right
tool is a plain read off the context, `context.TryGetQuery("sort", out var sort)`, which is documented
in the helper table two sections earlier but was never connected to this case.

---

## Steps to reproduce

1. Copy the sample from `docs/docs/endpoints.mdx` § "Validating several inputs at once" — or from the
   `<example>` on `BindingValidator` — into a project referencing `Synapse.Endpoints`.
2. `dotnet build`.

---

## Expected behavior

A sample published as the introduction to the type compiles.

---

## Actual behavior

`error CS0453` on the `QueryOptional<string>` line.

---

## Code sample

```csharp
// Published, and does not compile:
var v = context.Validate();
v.QueryOptional<string>("sort", out var sort);   // CS0453

// Optional value type — what the constrained members are for:
v.QueryOptional<int>("size", out var size);      // null when absent, reported when unparsable

// Optional string — nothing can fail, so it does not belong to the collector:
context.TryGetQuery("sort", out var sort);
```

---

## Library version

`feat/synapse-endpoints`

## .NET version

.NET 10.0

## Operating system

macOS (platform-independent)

---

## Additional context

### Root cause

The samples were written alongside the API rather than compiled against it. Neither the documentation
build (Docusaurus does not compile C# in fenced blocks) nor the XML documentation build (a `<code>`
block inside `<example>` is opaque text to Roslyn) can catch a code sample that does not compile, so
nothing in CI was ever going to.

The choice of constraint is not the bug and has not changed. `struct, IParsable<T>` is what expresses
"absent or a parsed value" for a value type, and the reference-typed counterpart cannot be an overload
of the same name — two generic methods differing only in their constraints are a duplicate signature
(CS0111), the same reason the enum readers are named `…Enum` rather than overloaded.

### Resolution

Both samples now read `QueryOptional<int>` and reach for the context directly for the optional string.
`docs/docs/endpoints.mdx` gained a paragraph stating the rule behind the constraint — the collector
carries only readers that can fail — so the asymmetry reads as a design decision rather than a missing
overload. No API changed: adding a `QueryOptionalString` would have grown the surface for an operation
that cannot produce an error, which is the one thing the type is for.

**Verification.** The corrected sample was compiled verbatim against `src/Synapse.Endpoints` in a
throwaway project: the original produced CS0453, the replacement builds clean. `cd docs && pnpm build`
passes with no broken links.
