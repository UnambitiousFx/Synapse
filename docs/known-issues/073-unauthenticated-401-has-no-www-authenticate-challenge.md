# [Bug]: A pipeline-denied request returns 401 without the `WWW-Authenticate` challenge RFC 9110 requires

**Severity:** Medium
**Area:** AspNetCore mapping
**Discovered on:** `main`, .NET 10, during an RFC-compliance audit of the web integration packages
**Status:** ✅ **Resolved** on `fix/aspnetcore-rfc-auth-responses` — see [Resolution](#resolution).

> **TL;DR.** `IHttpInvoker` and `IMvcInvoker` now add a `WWW-Authenticate` challenge
> (`SynapseAspNetCoreOptions.AuthenticationChallenge`, `Bearer` by default) to every failure response that resolves to 401.

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
is present, writes `WWW-Authenticate` with `SynapseAspNetCoreOptions.AuthenticationChallenge` (`Bearer` by
default, `null` to disable), set through `AddSynapseAspNetCore(o => ...)`. The value is configured rather than
read from the default authentication scheme because a scheme's registration name (say `ApiJwt`) is not the HTTP
authentication method a client can answer. Checking the final status keeps it correct with any registered
`IFailureHttpMapper`; the success path is untouched.

**Verification.** `HttpInvokerTests` (`InvokeAsync_WhenUnauthenticated_Returns401WithBearerChallenge`,
`..._WritesConfiguredChallenge`, `InvokeAsync_WhenForbidden_Returns403WithoutChallenge`) and
`MvcInvokerTests.InvokeAsync_WhenUnauthenticated_Returns401WithBearerChallenge`.
