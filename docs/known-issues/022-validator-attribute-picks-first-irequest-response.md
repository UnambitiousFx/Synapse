# [Bug]: `[Validator]` derives the response type from the first `IRequest<T>` found

**Severity:** Low
**Area:** `Synapse.Generator` / Validation
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — multiplicity is now detected; ambiguous requests emit diagnostic MDG011
and are skipped rather than guessed.

---

## Describe the bug

For a `[Validator]`, the generator derives the request's response type by taking the **first**
`IRequest<TResponse>` found while enumerating the request type's `AllInterfaces`. If a request
implements more than one `IRequest<TResponse>`, the response type chosen is whichever the interface
enumeration yields first — effectively non-deterministic — so the emitted
`RegisterValidator<TValidator, TRequest, TResponse>` may bind the wrong response type.

(A request implementing only the non-generic `IRequest` correctly yields `null` and the two-argument
overload is emitted, which is fine.)

---

## Steps to reproduce

1. Declare a request implementing multiple `IRequest<T>` and a validator for it:

   ```csharp
   public sealed record MultiReq : IRequest<Foo>, IRequest<Bar>;

   [Validator]
   public sealed class V : IRequestValidator<MultiReq> { /* ... */ }
   ```

2. Build so the generator runs.

---

## Expected behavior

Either the correct response type (the one the handler produces) is selected deterministically, or the
ambiguity is reported as a generator diagnostic.

---

## Actual behavior

The generator emits `builder.RegisterValidator<V, MultiReq, Foo>()` (or `Bar`) depending on interface
ordering. When the chosen response disagrees with the handler's response, the registration is wrong
(compile error or mismatched runtime binding), with no diagnostic.

---

## Root cause

`src/Synapse.Generator/SynapseGenerator.cs` (≈ line 609) returns the first matching
`IRequest<TResponse>` from `AllInterfaces` without checking for multiplicity.

---

## To address

- If more than one `IRequest<TResponse>` is present, emit a generator diagnostic rather than guessing.
- Multiple `IRequest<T>` on one request is itself questionable; consider whether the abstraction should
  forbid it.

## Resolution

`GetRequestResponseType` (`src/Synapse.Generator/SynapseGenerator.cs`) now scans **all**
`IRequest<TResponse>` interfaces and dedupes by emit name. When two or more **distinct** response types
are found it returns `Ambiguous = true`; `GetValidatorDetail` then yields a `ValidatorScan` with null
`Detail` and the request name in `AmbiguousResponseRequest`, which the source-output consumer reports as
the new **MDG011** (`error`) diagnostic and **skips** the registration. A request implementing a single
`IRequest<TResponse>` (even repeated via inheritance) is unaffected; a request implementing only the
non-generic `IRequest` still yields the two-argument overload.

Covered by `ValidatorAttribute_RequestWithMultipleIRequest_EmitsMDG011AndNoRegistration` in
`test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs`.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
