# [Bug]: An `internal` constructor in a referenced assembly is treated as accessible

**Severity:** Low
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `ResolveConstructionStrategy` accepted any constructor declared `public` **or**
> `internal`, with no check on which assembly declared it. A message type in a referenced contracts
> assembly whose parameterless constructor is `internal` was therefore reported as having one, and the
> generated binder emitted `new Contracts.ExternalQuery()` — `CS1729` in the consuming assembly, in
> code the consumer cannot edit.

---

## Describe the bug

The generator picks how to construct a bound message: `new T()` when a parameterless constructor is
available, otherwise a call to the widest constructor. "Available" was decided by declared
accessibility alone:

```csharp
.Where(c => !c.IsStatic && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
```

`internal` is accessible from the assembly that *declares* it, not from the one being compiled.
Including it unconditionally is right for a message declared in the same project — the common case, and
presumably why it is there — and wrong for one that arrives as a reference.

A contracts assembly that keeps an `internal` parameterless constructor for serialization while
exposing a public one for callers is a normal shape, and it produced the worst combination: the
generator saw a parameterless constructor, preferred it over the public one, and emitted a call to
something inaccessible.

---

## Steps to reproduce

1. Put a message type in a separate assembly with `internal T() { }` plus a public constructor.
2. Reference that assembly and declare an endpoint bound to the message.
3. Build.

---

## Expected behavior

The inaccessible constructor is ignored and the accessible one is used.

---

## Actual behavior

```
error CS1729: 'ExternalQuery' does not contain a constructor that takes 0 arguments
```

pointing into `SynapseEndpointBinders.g.cs`.

---

## Code sample

```csharp
// Contracts.dll
public sealed class ExternalQuery : IRequest
{
    internal ExternalQuery() { }
    public ExternalQuery(string name) { Name = name; }
    public string? Name { get; set; }
}

// The consuming app
[Get("/externals")]
public sealed class ExternalEndpoint : Endpoint<ExternalQuery>;

// Emitted before the fix:  var message = new global::Contracts.ExternalQuery();   // CS1729
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

An accessibility *modifier* was read as an accessibility *fact*. `IsSymbolAccessibleWithin` exists for
exactly this and was not used; the sibling case — no accessible constructor at all — was already
acknowledged in a comment two lines below, so the question had been considered for one shape and not
the other.

The realism is low, which is why this is filed Low: a message type usually lives beside its handler,
and an `internal` constructor on a cross-assembly contract is unusual. But the failure is a hard build
error in generated code with no diagnostic to explain it, and the check costs one comparison.

`InternalsVisibleTo` is deliberately not consulted. It would make the accepted set larger and correct
in more cases, at the price of a rule whose answer depends on an attribute in a third assembly; the
public constructor that the fix falls back to is always there in the shapes that matter.

### Resolution

```csharp
.Where(c => !c.IsStatic &&
            (c.DeclaredAccessibility == Accessibility.Public ||
             (c.DeclaredAccessibility == Accessibility.Internal &&
              SymbolEqualityComparer.Default.Equals(c.ContainingAssembly, compilationAssembly))))
```

An internal constructor in the compilation under analysis behaves exactly as before; one from a
reference is now skipped, so the type falls through to the widest accessible constructor — the same
path a positional record already takes.

**Verification.** `BinderConstructionShapeTests.Generate_ForAnInternalConstructorInAReferencedAssembly_DoesNotCallIt`
compiles the message into a real separate assembly with `GeneratorHarness.CompileToReference`, then
asserts the emitted binder does not call the parameterless constructor and that the result compiles
against that reference. Two harness overloads were added to make a referenced assembly testable at all
for emission (`GetFileWithReferences`, `AssertGeneratedCompilesWithReferences`) — the existing ones
only took a bare source string, which is why this shape had never been exercised.
