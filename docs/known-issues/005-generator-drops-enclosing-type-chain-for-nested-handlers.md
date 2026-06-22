# [Bug]: Generator emits uncompilable name for nested handler/behavior classes

**Severity:** High
**Area:** `Synapse.Generator`
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

---

## Describe the bug

The source generator composes a handler/behavior type name as `"{Namespace}.{ClassName}"`, where
`ClassName` is the bare type identifier and `Namespace` is resolved by walking up to the enclosing
namespace declaration. The **enclosing-type chain is never captured**, so any handler or behavior
declared as a nested class produces a registration referencing a type that does not exist, and the
generated `RegisterGroup.g.cs` fails to compile.

---

## Steps to reproduce

1. Declare a handler as a nested class:

   ```csharp
   namespace MyApp.Features;

   public static class Tasks
   {
       public sealed class CreateHandler : IRequestHandler<CreateTaskCommand, CreateTaskResult>
       {
           public ValueTask<Result<CreateTaskResult>> HandleAsync(
               CreateTaskCommand request, IContext context, CancellationToken ct) => /* ... */;
       }
   }
   ```

2. Build the project so the generator runs.

---

## Expected behavior

The generator emits `builder.RegisterRequestHandler<global::MyApp.Features.Tasks.CreateHandler, ...>()`
and the project compiles.

---

## Actual behavior

The generator emits `global::MyApp.Features.CreateHandler` — the `Tasks.` segment is missing — so
`RegisterGroup.g.cs` fails with **CS0234 / CS0246** ("the type or namespace name does not exist").

---

## Root cause

- `FullHandlerTypeName => $"{Namespace}.{ClassName}"` (`src/Synapse.Generator/HandlerDetail.cs:44`)
- `FullBehaviorTypeName => $"{Namespace}.{ClassName}"` (`src/Synapse.Generator/BehaviorDetail.cs:71`)
- `ClassName` is `classDeclaration.Identifier.ValueText` — the simple identifier only
  (`src/Synapse.Generator/SynapseGenerator.cs:347` / `:416`)
- `GetNamespace()` walks straight to the `NamespaceDeclaration`, skipping enclosing types
  (`src/Synapse.Generator/BaseTypeDeclarationSyntaxExtensions.cs:19-24`)

No code path records the chain of enclosing types between the class and its namespace.

---

## To address

- Capture the full metadata/display name from the `INamedTypeSymbol` (e.g.
  `ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`) instead of recomposing
  `Namespace + "." + ClassName` from syntax, which yields the correct nested name and `global::`
  prefix for free.
- Alternatively, build the enclosing-type chain by walking `ContainingType` and join with `.`.
- Add a generator test with a nested handler and a nested behavior asserting the emitted name
  includes the enclosing type.

## Resolution

The detail structs now carry the **fully-qualified type name captured from the `INamedTypeSymbol`**
instead of recomposing `Namespace + "." + ClassName` from syntax.

**1. A display format that keeps the enclosing-type chain but omits generics.**
`SymbolDisplayFormat.FullyQualifiedFormat` would have worked for nested *closed* types, but it also
emits the type parameters (`global::MyApp.MyBehavior<T1, T2>`) — and
`EmitOpenGenericBehaviorRegistrations` needs the bare base name so it can close it itself
(`<{req}>`). So a custom format is used (`SynapseGenerator.FullyQualifiedNoGenericsFormat`):

```csharp
new SymbolDisplayFormat(
    SymbolDisplayGlobalNamespaceStyle.Included,
    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
    SymbolDisplayGenericsOptions.None);
```

This yields `global::MyApp.Features.Tasks.CreateHandler` for nested closed types and
`global::MyApp.MyBehavior` (no `<…>`) for open generics.

**2. The name is captured at scan time and stored on each detail.**
`HandlerDetail`, `BehaviorDetail`, and `ValidatorDetail` (which had the same latent bug) each gained a
`FullyQualifiedName` property; `FullHandlerTypeName` / `FullBehaviorTypeName` / `FullValidatorTypeName`
now return it. `ClassName` / `Namespace` are kept — still used for diagnostics and registration ordering.
`GlobalizeSimpleType` already no-ops on a leading `global::`, so the value flows through `GlobalizeType`
unchanged.

**Verification.** Covered by `test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs`:
`NestedRequestHandler_…`, `NestedClosedBehavior_…`,
`NestedOpenGenericBehavior_EmitsEnclosingTypeWithoutStrayTypeParameters` (guards the generics-omission
requirement), and `NestedValidator_…`. Full solution builds clean.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
