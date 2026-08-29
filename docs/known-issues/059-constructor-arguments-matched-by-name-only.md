# [Bug]: Constructor arguments are matched by name only, never by type

**Severity:** Medium
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** A constructor parameter was matched to a bound property by name, case-insensitively, and
> the property's local was passed to it with no check that it converts. An `int? Page` property and an
> `int page` parameter produced `new Query(valuePage)` — `CS1503` — and a `string?` property into a
> non-nullable `string` parameter produced `CS8604`, which fails a `TreatWarningsAsErrors` build on code
> the consumer cannot edit. The match is now resolved during analysis, where both types are still
> symbols and the conversion can actually be classified.

---

## Describe the bug

For a message with no parameterless constructor, the binder calls the widest constructor and fills each
parameter from the property that shares its name:

```csharp
argumentExpressions.Add(propertyByName.TryGetValue(parameter.Name, out var property)
    ? ValueLocal(property)
    : parameter.IsReferenceType ? "default!" : "default");
```

Case-insensitive name matching is right — a positional record's parameter and its property differ only
in case. What was missing is that the local being passed has the *property's* type, which is not
necessarily the parameter's:

- `int? Page` into `int page`: `int?` has no implicit conversion to `int`, so **CS1503**.
- `string? Name` into `string name`: legal but **CS8604** ("possible null reference argument"), which
  is an error for any consumer building with `TreatWarningsAsErrors` — and generated code is precisely
  what a consumer cannot annotate or suppress locally.

Both shapes are unremarkable C#: a class with a constructor that takes the non-nullable form and a
nullable property, or a record whose property was widened without the constructor following.

The emitter could not have checked this. It works from `BindablePropertyModel`, which carries type
*names* as strings, and comparing display strings is not a conversion check — `int` versus `int?`
versus `global::System.Nullable<int>` are the same type under three spellings, and nullable reference
annotations do not survive the format the model uses at all.

---

## Steps to reproduce

1. Declare a message with a constructor parameter whose type differs from the same-named property's
   (`int page` / `int? Page`, or `string name` / `string? Name`).
2. Bind it from an endpoint on a bodyless verb.
3. Build.

---

## Expected behavior

The parameter is not fed from a property it cannot accept: it takes its default, and the property is
applied after construction, which is always available — every bindable property is either settable or
on a record, which is what SYNE011 guarantees.

---

## Actual behavior

```
error CS1503: Argument 1: cannot convert from 'int?' to 'int'
warning CS8604: Possible null reference argument for parameter 'name'
```

in `SynapseEndpointBinders.g.cs`.

---

## Code sample

```csharp
public sealed class PageQuery : IRequest
{
    public PageQuery(int page) { Page = page; }
    public int? Page { get; set; }
}

[Get("/pages")]
public sealed class PageEndpoint : Endpoint<PageQuery>;

// Emitted before:  var message = new global::TestNs.PageQuery(valuePage);   // int? -> int, CS1503
// Emitted after:   var message = new global::TestNs.PageQuery(default);
//                  message.Page = valuePage;
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

The matching lived in the emitter, which is the wrong side of the boundary for the question it was
asking. Names are strings and match fine there; types are not, and by then they had been flattened to
display strings — deliberately, since the pipeline state has to stay equatable and cheap to compare for
incrementality. So the emitter asked the only question it could and assumed the answer to the one it
could not.

### Resolution

The match moved to analysis, where `IPropertySymbol` and `IParameterSymbol` are both to hand:

```csharp
var conversion = compilation.ClassifyCommonConversion(property.Type, parameter.Type);
if (!conversion.IsIdentity && !conversion.IsImplicit) { return null; }

if (property.Type.IsReferenceType &&
    property.Type.NullableAnnotation == NullableAnnotation.Annotated &&
    parameter.Type.NullableAnnotation == NullableAnnotation.NotAnnotated)
{
    return null;   // compiles, but CS8604
}
```

The result is carried as `ConstructorParameterModel.MatchedPropertyName` — a string, so the pipeline
state stays equatable — and the emitter simply reads it instead of re-deriving it. That also removes
the duplicate matching that `ResolveConstructorConsumption` and `EmitPrimaryConstructorCall` each did
with a comment promising they agreed; there is now one answer, computed once.

A parameter left unmatched falls back to its own declared default where it has one (see
[060](060-constructor-parameter-defaults-are-discarded.md)) and to `default`/`default!` otherwise, and
the property is applied after construction as it would be for any non-constructor property. No
diagnostic is reported: the resulting object is correct, and the alternative — a new diagnostic ID for
a shape that now simply works — would be noise.

**Verification.** `BinderConstructionShapeTests.Generate_ForAConstructorParameterOfADifferentType_DoesNotPassThePropertyToIt`
is a theory over both shapes (`int?`/`int` and `string?`/`string`) asserting the property is not passed
to the constructor, is applied afterwards, and that the generated code compiles. Both cases fail
against the previous behaviour — the first as an error, the second as the warning
`AssertGeneratedCompiles` would not have caught, which is why the assertion is on the emitted text and
not only on compilation.
