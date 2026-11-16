# [Bug]: A pipeline-denied request returns 401 without the `WWW-Authenticate` challenge RFC 9110 requires

**Severity:** Medium
**Area:** AspNetCore mapping
**Discovered on:** `main`, .NET 10, during an RFC-compliance audit of the web integration packages
**Status:** ✅ **Resolved** on `fix/aspnetcore-rfc-auth-responses` — see [Resolution](#resolution).

> **TL;DR.** `IHttpInvoker` and `IMvcInvoker` now add a `WWW-Authenticate` challenge (the app's default
> challenge scheme, `Bearer` when none is registered) to every failure response that resolves to 401.

---

## Describe the bug

`AddSynapseAuthorization()` denies an anonymous caller with an `UnauthenticatedFailure`, which
`DefaultFailureHttpMapper` maps to `401 Unauthorized` with a Problem Details body. The response carries no
`WWW-Authenticate` header. RFC 9110 §15.5.2 says a server generating a 401 "MUST send" one.

The denial happens in the mediator pipeline, not in ASP.NET Core's authentication middleware, so nothing
else in the request writes the challenge.

---

## Steps to reproduce

1. Register `AddSynapseAuthorization()` and `AddSynapseAspNetCore()`.
2. Map an endpoint with `IHttpInvoker.InvokeAsync` for a request that does not implement `IAllowAnonymous`.
3. Call it without credentials.

---

## Expected behavior

`401 Unauthorized` with `WWW-Authenticate: Bearer` (or the app's challenge scheme) and the Problem Details body.

---

## Actual behavior

`401 Unauthorized` with the Problem Details body and no `WWW-Authenticate` header.

---

## Code sample

```csharp
app.MapGet("/loans", (IHttpInvoker invoker, CancellationToken ct) =>
    invoker.InvokeAsync(new ListLoans(), ct)); // anonymous → 401, no challenge
```

---

## Library version

`main` (after 4.0.1 of `UnambitiousFx.Functional.AspNetCore`)

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

`HttpInvoker` / `MvcInvoker` returned the mapper's result as is. `DefaultFailureHttpMapper` writes no
headers, and a custom mapper is not required to either.

### Resolution

On every failure path the invokers wrap the result (`ChallengingHttpResult` / `ChallengingActionResult`).
The wrapper registers a `Response.OnStarting` callback that, when the final status is 401 and no challenge
is present, writes `WWW-Authenticate` with the name of the app's default challenge scheme
(`IAuthenticationSchemeProvider`), or `Bearer` when no authentication is registered. Checking the final
status keeps it correct with any registered `IFailureHttpMapper`; the success path is untouched.

**Verification.** `HttpInvokerTests` (`InvokeAsync_WhenUnauthenticated_Returns401WithBearerChallenge`,
`..._ChallengesWithDefaultChallengeScheme`, `InvokeAsync_WhenForbidden_Returns403WithoutChallenge`) and
`MvcInvokerTests.InvokeAsync_WhenUnauthenticated_Returns401WithBearerChallenge`.
