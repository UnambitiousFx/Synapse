# 006 — Form, multipart and file binding

|  |  |
|---|---|
| **Status** | ✅ Shipped |
| **Priority** | Medium-High |
| **Area** | Binding / Generator |
| **Tiers** | All bound tiers |
| **Breaking** | No — additive |

## Problem

The package cannot accept a file upload or an HTML form post. `application/x-www-form-urlencoded`
and `multipart/form-data` are not recognised anywhere in the binder, the helpers or the analyzer.

Any endpoint that takes a file has to drop to `RawEndpoint` and read `context.Request.Form` by hand,
losing binding, error accumulation and OpenAPI metadata in one step.

## Current state

- `src/Synapse.Endpoints.Generator/EndpointsGenerator.cs` recognises exactly four sources:
  `FromRouteAttribute`, `FromQueryAttribute`, `FromHeaderAttribute` (both the MVC one and Synapse's
  own) and `FromBodyAttribute`.
- `src/Synapse.Endpoints/Binding/BindingHelpers.cs` reads route values, query values, headers and a
  JSON body. No `ReadFormAsync`, no `IFormFile`, no `IFormCollection`.
- `HttpContextBindingExtensions` exposes `BodyAsync<T>` (JSON only) and nothing form-shaped.
- `BindingHelpers.ReadJsonBodyAsync` rejects a non-JSON content type outright with a `body` failure,
  and the `Accepts`-driven consumes matcher policy answers `415` for a form content type before the
  binder is even reached.

## What you cannot write today

*(This section narrates the pre-shipped state, including the old five-rule numbering, as a historical
record of the problem — not the situation today. See [Messages & binding → Forms and files](../../docs/endpoints/high-level/messages.mdx#forms-and-files)
for what actually shipped, against the current six rules.)*

Attach a file to a task — one route, one file, one field:

```csharp
public sealed record UploadAttachmentCommand : IRequest<AttachmentCreated>
{
    public required Guid TaskId { get; init; }      // rule 3: matches {taskId}

    // rule 5: POST carries a body, and nothing else claimed these two — so both are
    // resolved as *JSON body* properties.
    public required IFormFile File { get; init; }
    public required string Caption { get; init; }
}

[Post("/tasks/{taskId:guid}/attachments")]
public sealed class UploadAttachmentEndpoint : Endpoint<UploadAttachmentCommand, AttachmentCreated>;
```

It compiles, with a warning, and cannot work:

- `SYNE008`: `IFormFile` is missing from every `JsonSerializerContext` — correct, and unfixable,
  because an interface over a buffered stream is not a JSON-serializable type.
- The endpoint declares `Accepts<UploadAttachmentCommand>("application/json")`, so a
  `multipart/form-data` request is answered `415` by the consumes matcher policy before the binder
  runs. There is no request that reaches the handler.

So the whole endpoint drops a tier:

```csharp
[Post("/tasks/{taskId:guid}/attachments")]
public sealed class UploadAttachmentEndpoint : RawEndpoint
{
    public override void Configure(IRawEndpointBuilder builder)
    {
        // The multipart schema is not describable through Accepts<T>, so the request half of
        // this endpoint's OpenAPI document is written by hand through Raw, or not at all.
        builder.Produces<AttachmentCreated>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Raw(route => route.DisableAntiforgery());
    }

    public override async ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken ct)
    {
        if (!context.TryGetRoute<Guid>("taskId", out var taskId))
        {
            return TypedResults.BadRequest("taskId must be a GUID");
        }

        // Hand-rolled, and none of it accumulates: three bad inputs are three round trips.
        if (!context.Request.HasFormContentType)
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var form = await context.Request.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null)
        {
            return TypedResults.BadRequest("file is required");
        }

        var caption = form["caption"].ToString();

        // …and now dispatch by hand too, because the message could not be bound.
        var invoker = context.Service<IHttpInvoker>();
        …
    }
}
```

Binding, error accumulation, declarative responses and OpenAPI metadata are all lost in one step —
for a route whose only unusual property is its content type.

## Proposed API

```csharp
// Attribute, alongside FromHeaderAttribute
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FromFormAttribute : Attribute { public string? Name { get; } }

// Helpers
public static ValueTask<BindResult<IFormCollection>> FormAsync(this HttpContext context, CancellationToken ct = default);
public static bool TryGetFormFile(this HttpContext context, string name, out IFormFile? file);

// Validator
public bool Form<T>(string name, out T value) where T : IParsable<T>;
public bool FormFile(string name, out IFormFile file);
```

Generator work: a property typed `IFormFile` / `IFormFileCollection`, or annotated `[FromForm]`,
binds from the form; the endpoint then declares `Accepts<TRequest>("multipart/form-data")` instead
of `application/json`, and `IEndpointBinder<T>.ReadsRequestBody` must stay accurate.

### With the proposal

The message says where each value comes from, and the endpoint stays at the high level:

```csharp
public sealed record UploadAttachmentCommand : IRequest<AttachmentCreated>
{
    public required Guid TaskId { get; init; }                        // route, unchanged

    [FromForm] public required IFormFile File { get; init; }
    [FromForm("caption")] public required string Caption { get; init; }
}

[Post("/tasks/{taskId:guid}/attachments")]
public sealed class UploadAttachmentEndpoint : Endpoint<UploadAttachmentCommand, AttachmentCreated>
{
    public override void Configure(IEndpointBuilder<AttachmentCreated> builder)
    {
        builder.Created(created => $"/tasks/{created.TaskId}/attachments/{created.AttachmentId}")
               .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
```

`Accepts` is declared as `multipart/form-data` because the binder says so, `SYNE008` stays silent
(nothing is JSON-deserialized), and a request missing both form fields gets one response:

```json title="POST /tasks/{id}/attachments with an empty body"
{
  "status": 400,
  "errors": {
    "File":    ["The form file is required."],
    "caption": ["The form value is required."]
  }
}
```

(`File` carries no name argument on its `[FromForm]`, so it falls back to the property name, capital
`F`; `Caption`'s `[FromForm("caption")]` sets its key explicitly, lowercase, as written. A file has no
parse step, so its message is `"…is required."` only — never `"…is not a valid T."` — which is why it
reads "form file" rather than "form value".)

And if the feature is deliberately deferred instead, the low tier at least stops being hand-rolled:

```csharp
var form = await context.FormAsync(ct);
if (!form.IsSuccess) { return form.Problem(); }     // 415 or 400, decided in one place

var v = context.Validate();
v.FormFile("file", out var file);
v.Form<string>("caption", out var caption);
if (!v.IsValid) { return v.Problem(); }
```

## Acceptance criteria

- [x] `multipart/form-data` and `application/x-www-form-urlencoded` both bind.
- [x] Content type is declared correctly so the consumes matcher policy answers `415`, not `400`.
- [x] Form binding participates in error accumulation like every other source.
- [x] A new diagnostic reports `[FromForm]` on a bodyless verb (mirrors `SYNE007`) — shipped as
      `SYNE017`, and fires equally for a bare file-typed property, since rule 3 infers the source
      from the type the same way an explicit `[FromForm]` states it.
- [x] `[FromForm]` and `[FromBody]` on the same message is a diagnostic, not a runtime surprise —
      shipped as `SYNE018`.
- [ ] OpenAPI declares the multipart schema — tracked separately as feature 018 (OpenAPI parameter
      metadata), not part of this feature's scope. What ships here declares the correct content
      types and no schema, which is what the criterion above actually needs.
- [x] Native AOT path verified — form reading is reflection-free, and `SYNE008`/`SYNE015` are scoped
      to messages that are actually JSON-deserialized, so neither fires for a form-bound one. See
      [Native AOT](../../docs/endpoints/reference/native-aot.mdx#form-binding-needs-no-registration-at-all).
