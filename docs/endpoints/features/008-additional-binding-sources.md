# 008 — Additional binding sources (claims, cookies, services)

|  |  |
|---|---|
| **Status** | 🔴 Missing |
| **Priority** | Medium |
| **Area** | Binding / Generator |
| **Tiers** | All bound tiers |
| **Breaking** | No — additive |

## Problem

Four sources are supported: route, query, header, body. Three common ones are not:

- **Claims.** Every authenticated endpoint needs the caller's id. Today it is read from
  `context.User` inside the handler, or worse, bound from a header the client controls.
- **Cookies.** No reader at all, at either tier.
- **Services.** Endpoints are singletons with no constructor injection, so every dependency is a
  `context.Service<T>()` call inside the handler body.

The claim case is the one with a security edge: the ergonomic path (a header) is the wrong one, and
nothing steers the user away from it.

## Current state

`src/Synapse.Endpoints.Generator/EndpointsGenerator.cs` recognises only:

```
Microsoft.AspNetCore.Mvc.FromRouteAttribute
Microsoft.AspNetCore.Mvc.FromQueryAttribute
Microsoft.AspNetCore.Mvc.FromHeaderAttribute
UnambitiousFx.Synapse.Endpoints.FromHeaderAttribute
Microsoft.AspNetCore.Mvc.FromBodyAttribute
```

`grep -rn "Cookie\|Claim" src/Synapse.Endpoints src/Synapse.Endpoints.Generator` returns nothing
binding-related. `src/Synapse.Endpoints/Binding/BindingHelpers.cs` has no cookie reader.

## What you cannot write today

An endpoint that archives the *caller's* task — the id comes from the token, the session from a
cookie:

```csharp
public sealed record ArchiveTaskCommand : IRequest
{
    public required Guid TaskId { get; init; }

    [FromClaim("sub")]  public required Guid UserId { get; init; }
    // CS0246: The type or namespace name 'FromClaimAttribute' could not be found

    [FromCookie("sid")] public string? Session { get; init; }
    // CS0246: The type or namespace name 'FromCookieAttribute' could not be found
}
```

Both spellings have to become something else, and every option is worse:

```csharp
public sealed record ArchiveTaskCommand : IRequest
{
    public required Guid TaskId { get; init; }

    // Option A — the ergonomic one, and the wrong one. It compiles, binds, appears in the
    // OpenAPI document as a client-supplied parameter, and lets any caller archive any user's
    // task by editing a header.
    [FromHeader("X-User-Id")] public required Guid UserId { get; init; }

    // Option B — correct, but the message now lies about its own inputs: UserId is required by
    // the handler and unset by the binder, so every other dispatch path must remember to stamp
    // it, and nothing checks that it did.
    [NotBound] [JsonIgnore] public Guid UserId { get; init; }
}
```

With option B the read moves into the handler, off the binding path entirely:

```csharp
// In the endpoint or the handler, per endpoint, forever:
var sub = context.User.FindFirst("sub")?.Value;
if (!Guid.TryParse(sub, out var userId))
{
    // Not a bad request — the caller is not authenticated. But this is the binding path's
    // vocabulary, so it becomes a 400 unless every endpoint remembers otherwise.
    return TypedResults.Unauthorized();
}

// And the cookie, which has no reader at either tier:
var session = context.Request.Cookies["sid"];
```

A property named `Session` with no attribute is worse still: on a bodyless verb, rule 4 binds it
**from the query string**, so `?session=…` silently wins and the cookie is never read.

## Proposed API

```csharp
[FromClaim("sub")]   public Guid   UserId  { get; init; }
[FromCookie("sid")]  public string? Session { get; init; }
```

with matching low-level helpers and validator methods:

```csharp
public static bool TryGetCookie(this HttpContext context, string name, out string? value);
public static bool TryGetClaim(this HttpContext context, string type, out string? value);

public bool Claim<T>(string type, out T value) where T : IParsable<T>;
public bool Cookie<T>(string name, out T value) where T : IParsable<T>;
```

`[FromServices]` is a separate decision — see [017](017-endpoint-dependency-injection.md). It is
listed here only so the source table is complete.

### With the proposal

The message declares its sources, and none of them are client-editable:

```csharp
public sealed record ArchiveTaskCommand : IRequest
{
    public required Guid TaskId { get; init; }              // route

    [FromClaim("sub")]  public required Guid UserId { get; init; }
    [FromCookie("sid")] public string? Session { get; init; }
}

[Post("/tasks/{taskId:guid}/archive")]
public sealed class ArchiveTaskEndpoint : Endpoint<ArchiveTaskCommand>
{
    public override void Configure(IEndpointBuilder builder)
    {
        builder.RequireAuthorization()
               .ProducesProblem(StatusCodes.Status401Unauthorized)
               .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
```

Behaviour that the attributes buy, and that the workarounds cannot:

```bash
$ curl -X POST localhost:5000/tasks/$id/archive                       # no token
401   # not a 400 — an absent required claim is an authentication failure, not a bad request

$ curl -X POST localhost:5000/tasks/$id/archive -H 'X-User-Id: …'     # forged header
# ignored: UserId is not a client-supplied parameter, and does not appear as one in
# /openapi/v1.json — only `sid` shows up, as a cookie parameter
```

And at the low level:

```csharp
var v = context.Validate();
v.Claim<Guid>("sub", out var userId);
v.Cookie<string>("sid", out var session);
```

## Acceptance criteria

- [ ] A missing required claim produces `401`, not `400` — a caller who is not authenticated has not
      sent a bad request. This is the one place the "everything accumulates into a 400" rule must not
      apply, and it needs a deliberate decision plus a test.
- [ ] Claim binding is excluded from OpenAPI parameters (it is not a client-supplied input).
- [ ] `[FromClaim]` on an endpoint with `AllowAnonymous` is a diagnostic.
- [ ] Cookie values appear in the OpenAPI document as cookie parameters.
