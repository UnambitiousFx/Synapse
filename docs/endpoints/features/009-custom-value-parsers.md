# 009 — Custom value parsers

|  |  |
|---|---|
| **Status** | 🔴 Missing |
| **Priority** | Medium |
| **Area** | Binding / Generator |
| **Tiers** | All bound tiers |
| **Breaking** | No — additive |

## Problem

A bound property must be a `string`, an enum, or expose `TryParse`. If the type is one you do not
own — a value object from a shared package, a third-party id type, a `DateOnly` wrapper — there is
no way to teach the binder how to read it. `SYNE012` tells you to add a `TryParse` overload to a
type you cannot edit.

## Current state

`src/Synapse.Endpoints.Generator/Diagnostics/EndpointDiagnostics.cs` — `SYNE012`:

> Property '{0}' on '{1}' has type '{2}', which is not string, not an enum, and has no public static
> `TryParse(string, out {2})` or `TryParse(string, IFormatProvider, out {2})` method, so its bound
> value cannot be parsed. Add either overload to '{2}' […] or change the type of '{0}'.

Both suggestions require ownership of the type. `docs/known-issues/057` covers a related sharp edge
in the same area.

## What you cannot write today

A route keyed by a value object that lives in a shared package:

```csharp
public sealed record GetTenantUsageQuery : IRequest<UsageDto>
{
    // Acme.Platform.TenantId — a readonly struct in a NuGet package, with a From/ToString pair
    // and no TryParse.
    public required TenantId TenantId { get; init; }
}

[Get("/tenants/{tenantId}/usage")]
public sealed class GetTenantUsageEndpoint : Endpoint<GetTenantUsageQuery, UsageDto>;
```

```
error SYNE012: Property 'TenantId' on 'GetTenantUsageQuery' has type 'Acme.Platform.TenantId',
  which is not string, not an enum, and has no public static
  TryParse(string, out Acme.Platform.TenantId) […] method, so its bound value cannot be parsed.
  Add either overload to 'Acme.Platform.TenantId' — implementing IParsable<Acme.Platform.TenantId>
  supplies the second — or change the type of 'TenantId'.
```

Both remedies require owning the assembly. The extension-method escape does not work either — the
generator emits a call to a `public static` `TryParse` **on the type**, and an extension method
cannot supply one:

```csharp
public static class TenantIdExtensions
{
    // Compiles, and is never called: SYNE012 still fires, because the member the generated
    // binder needs is Acme.Platform.TenantId.TryParse.
    public static bool TryParse(this string raw, out TenantId value) { … }
}
```

So the message gives up its type, and the parse moves somewhere with no error accumulation:

```csharp
public sealed record GetTenantUsageQuery : IRequest<UsageDto>
{
    public required string TenantId { get; init; }      // stringly typed, all the way to the handler
}

// In the handler:
var tenant = TenantId.From(query.TenantId);   // throws for a bad value → 500, not a 400 naming
                                              // the field, unless every handler wraps it by hand
```

## Proposed API

An opt-in parser registered at compile time, so nothing becomes reflective:

```csharp
[EndpointValueParser]
public static partial class TenantIdParser
{
    public static bool TryParse(string? raw, IFormatProvider? provider, out TenantId value) { … }
}
```

The generator collects `[EndpointValueParser]` types, indexes them by target type, and emits calls to
them in place of the built-in `T.TryParse`. `SYNE012` then names the parser attribute as a third
option in its message.

Alternative worth weighing: honour `TypeConverter`. Cheaper for consumers, but reflective, so it
would have to be off by default under Native AOT — which is most of the point of this package.

### With the proposal

One parser per foreign type, written once in the consuming assembly:

```csharp
[EndpointValueParser]
public static partial class TenantIdParser
{
    public static bool TryParse(string? raw, IFormatProvider? provider, out TenantId value)
    {
        if (TenantId.IsValid(raw))
        {
            value = TenantId.From(raw!);
            return true;
        }

        value = default;
        return false;
    }
}
```

The message keeps its type and nothing else changes:

```csharp
public sealed record GetTenantUsageQuery : IRequest<UsageDto>
{
    public required TenantId TenantId { get; init; }    // SYNE012 satisfied by the parser
}
```

```bash
$ curl -s localhost:5000/tenants/not-a-tenant/usage
{"status":400,"errors":{"tenantId":["The route value is not a valid TenantId."]}}
```

A bad value is now the same accumulated `400` every other bound property produces, decided at the
edge instead of thrown from the handler — and the generated binder calls `TenantIdParser.TryParse`
directly, so nothing is resolved at request time.

## Acceptance criteria

- [ ] Parser resolution is compile-time only; nothing is looked up at request time.
- [ ] Two parsers for the same target type is a diagnostic, not a silent last-one-wins.
- [ ] A parser in a referenced assembly is discovered.
- [ ] `SYNE012`'s message lists the parser option.
- [ ] Works under Native AOT with no `IL2xxx`/`IL3xxx` warnings.
