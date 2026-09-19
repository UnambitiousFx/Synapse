# [Bug]: Constructor parameter defaults are discarded

**Severity:** Medium
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** For `record ListUsers(int Page = 1, string? Sort = "name")`, a request to `GET /users` with
> no query string answered `400` for `Page` — which has a default — and bound `Sort` to `null`, not
> `"name"`. Every constructor argument came from a local initialised to `default(T)`, and the declared
> defaults were never read. The locals now start at the declared default, and a parameter with a default
> makes its value optional.

---

## Describe the bug

Two symptoms from one cause, and the quiet one is worse.

**A nullable parameter's default was overwritten.** The presence flag that keeps an absent optional
value from clobbering something was switched off precisely for constructor-consumed properties:

```csharp
var presenceLocal = consumedByConstructor ? null : "has" + property.Name;
```

That is sound as far as it goes — a constructor argument is passed whether or not the value was present,
so the flag would never be read and an unread local is a warning in generated code. But it means the
argument is whatever the local holds, and the local was initialised to `default`. So `Sort` arrived as
`null`, silently, in a message whose author had written `= "name"` to say otherwise. The documentation
promised the opposite: a nullable property's missing value leaves "the property alone and binding
succeeds".

**A non-nullable parameter's default was treated as mandatory.** `int Page = 1` is non-nullable, so the
binder required it and answered `400` when it was absent — for a value the type had just said it could
do without.

Compounding both, `HasParameterlessConstructor` was reported `false` whenever the widest constructor
had any parameters, even when every one of them was optional and `new T()` would have compiled.

---

## Steps to reproduce

1. Declare `public sealed record ListUsers(int Page = 1, string? Sort = "name") : IRequest;`
2. Bind it from `[Get("/users")]`.
3. `GET /users` with no query string.

---

## Expected behavior

`Page` is `1` and `Sort` is `"name"`, and the request succeeds.

---

## Actual behavior

`400`, reporting that the `Page` query value is required. Supplying `?page=2` gets past that and binds
`Sort` to `null`.

---

## Code sample

```csharp
public sealed record ListUsers(int Page = 1, string? Sort = "name") : IRequest;

// Emitted before:
//     int valuePage = default;
//     if (!TryGetQuery(context, "Page", out var rawPage)) { validation.AddError("Page", "…required."); }
//     …
//     string? valueSort = default;
//     if (TryGetQuery(context, "Sort", out var rawSort)) { valueSort = rawSort; }
//     var message = new ListUsers(valuePage, valueSort);      // (0 -> 400) and (null, not "name")

// Emitted after:
//     int valuePage = (int)(1);
//     if (TryGetQuery(context, "Page", out var rawPage)) { …parse, error only if unparsable… }
//     string? valueSort = (string)("name");
//     if (TryGetQuery(context, "Sort", out var rawSort)) { valueSort = rawSort; }
//     var message = new ListUsers(valuePage, valueSort);
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

`ConstructorParameterModel` carried a name and whether the type was a reference type — enough to emit
`default!` versus `default` for an unmatched parameter, which is what it was built for
([044](044-endpoints-binder-default-for-unmatched-reference-constructor-parameter.md)). It never
carried `HasExplicitDefaultValue`, so no part of the emitter could know a default existed. The
optionality rules were written against property nullability and property initialisers, which is where
the documentation's "a property initialiser does not make a property optional" rule comes from;
constructor defaults are a third mechanism that the rules never covered, and they are the one mechanism
the compiler can actually enforce.

### Resolution

`ConstructorParameterModel.DefaultValueExpression` carries the default as a C# expression, and it is
used in two places: as the initial value of a matched property's local, and as the argument for a
parameter no property matches (its own default beats a synthesized `default`).

A local that starts at a declared default no longer demands presence — an absent value leaves the
default standing, while an unparsable one still reports, so `?page=abc` is still a `400`.

The default is rendered with a cast to the parameter's type, `(int)(1)` and `(string)("name")`, because
a primitive literal does not always assign to the type that declared it: `float f = 1.5f` round-trips
through `SymbolDisplay.FormatPrimitive` as `1.5`, which is a `double` and `CS0664` on assignment, and an
enum default arrives as its underlying integer. A default the compiler cannot express as a constant —
`Guid g = default` — has no `ExplicitDefaultValue` and becomes the `default` keyword, which is correct
for every type.

The documented binding rules gained the case: a value bound from a constructor parameter with a default
is optional, and the "a property initialiser does not make a property optional" paragraph now points at
a constructor default as the way to express one that works.

**Verification.** `BinderConstructionShapeTests.Generate_ForConstructorParametersWithDefaults_FallsBackToThemInsteadOfRequiringAValue`
asserts both locals start at their declared defaults and that no "required" error is emitted for either,
and that the result compiles. It fails against the previous behaviour on all three assertions.
