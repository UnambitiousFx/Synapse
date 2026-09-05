# [Bug]: A minimal-API `IAcceptsMetadata` with a `null RequestType` crashes the entire OpenAPI document

**Severity:** High
**Area:** AspNetCore mapping
**Discovered on:** `feat/synapse-endpoints`, .NET 10, while implementing the OpenAPI satellite package's form request-body schema (feature 018)
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `Microsoft.AspNetCore.OpenApi` 10.0.11 fails the *entire* OpenAPI document — every
> endpoint, `500` on `/openapi/v1.json` — whenever any endpoint carries an `IAcceptsMetadata` whose
> `RequestType` is `null`. The root cause is in the framework, not in Synapse, so the fix is a
> workaround that can be deleted if ASP.NET Core stops synthesising a `typeof(void)` body parameter;
> and the workaround lives in the **opt-in** `UnambitiousFx.Synapse.Endpoints.OpenApi` package, so an
> app that maps form endpoints and calls `AddOpenApi()` **without** referencing that package still
> has the broken document.

---

## Describe the bug

`Microsoft.AspNetCore.OpenApi` 10.0.11 (matches the installed `Microsoft.AspNetCore.App`
shared-framework runtime exactly; fetched and diffed against `github.com/dotnet/aspnetcore` tag
`v10.0.11`), combined with any endpoint whose
`Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata.RequestType` returns `null` and which has no
method-level body/form parameter for `EndpointMetadataApiDescriptionProvider` to key off instead
(true of any minimal-API delegate that takes only `HttpContext`, or any hand-rolled routing layer
shaped like Synapse's), fails the **whole** OpenAPI document, not just the operation that carries
that metadata.

`FormRequestMetadata.RequestType` has been deliberately `null` since feature 006 shipped (a message
holding an `IFormFile` has no JSON schema to describe), so **any** Synapse app with a form-bound
endpoint and `AddOpenApi()` wired up has a broken document today, independent of feature 018 or of
this plan.

---

## Steps to reproduce

Minimal repro, zero Synapse code (confirmed with a real `dotnet run` + `curl`, not just reasoning
about the source):

```csharp
// Program.cs — Sdk="Microsoft.NET.Sdk.Web", net10.0,
// <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.11" />
using Microsoft.AspNetCore.Http.Metadata;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapOpenApi();

app.MapPost("/probe", (Delegate)(async (HttpContext ctx) =>
{
    await ctx.Response.WriteAsync("ok");
})).WithMetadata(new ProbeAccepts());

app.Run();

sealed class ProbeAccepts : IAcceptsMetadata
{
    public Type? RequestType => null;
    public IReadOnlyList<string> ContentTypes { get; } =
        ["multipart/form-data", "application/x-www-form-urlencoded"];
    public bool IsOptional => false;
}
```

```
$ dotnet run --urls http://127.0.0.1:5099 &
$ curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5099/openapi/v1.json
500
```

Server log shows the stack trace below, verbatim down to the line numbers.

On this branch, the same crash reproduces from real Synapse code: map any endpoint whose message
has a form-bound property (`[FromForm]`, or a bare `IFormFile`) alongside `AddOpenApi()` with no
call to `AddSynapseEndpointsOpenApi()`, and request `/openapi/v1.json`.

---

## Expected behavior

An endpoint that declares a request body with no describable JSON schema (a form body, a raw
stream) should not prevent the framework from describing every *other* endpoint in the document.

## Actual behavior

`System.Text.Json.Schema.JsonSchemaExporter` is asked to describe `System.Void` and throws
unconditionally; nothing in the call chain catches it, so `GetOpenApiDocumentAsync` fails outright
and `/openapi/v1.json` returns `500` for the whole application.

---

## Code sample

```csharp
// EndpointMetadataApiDescriptionProvider.cs, inside CreateApiDescription (dotnet/aspnetcore v10.0.11)
if (!hasBodyOrFormFileParameter)
{
    var acceptsRequestType = acceptsMetadata.RequestType;
    var parameterDescription = new ApiParameterDescription
    {
        Name = acceptsRequestType is not null ? acceptsRequestType.Name : typeof(void).Name,
        ModelMetadata = CreateModelMetadata(acceptsRequestType ?? typeof(void)),
        Source = BindingSource.Body,
        Type = acceptsRequestType ?? typeof(void),
        IsRequired = !isOptional,
    };
    apiDescription.ParameterDescriptions.Add(parameterDescription);
}
```

**Exact stack trace observed** (from a from-scratch, zero-Synapse repro):

```
System.InvalidOperationException: The type 'System.Void' is invalid for serialization or
deserialization because it is a pointer type, is a ref struct, or contains generic parameters
that have not been replaced by specific types.
   at System.Text.Json.ThrowHelper.ThrowInvalidOperationException_CannotSerializeInvalidType(Type typeToConvert, Type declaringType, MemberInfo memberInfo)
   at System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
   at System.Text.Json.Serialization.Metadata.JsonTypeInfoResolverChain.GetTypeInfo(Type type, JsonSerializerOptions options)
   at System.Text.Json.Serialization.Metadata.JsonTypeInfoResolverWithAddedModifiers.GetTypeInfo(Type type, JsonSerializerOptions options)
   at System.Text.Json.JsonSerializerOptions.GetTypeInfoNoCaching(Type type)
   at System.Text.Json.JsonSerializerOptions.CachingContext.CreateCacheEntry(Type type, CachingContext context)
   --- End of stack trace from previous location ---
   at System.Text.Json.JsonSerializerOptions.GetTypeInfoInternal(Type type, Boolean ensureConfigured, Nullable`1 ensureNotNull, Boolean resolveIfMutable, Boolean fallBackToNearestAncestorType)
   at System.Text.Json.Schema.JsonSchemaExporter.GetJsonSchemaAsNode(JsonSerializerOptions options, Type type, JsonSchemaExporterOptions exporterOptions)
   at Microsoft.AspNetCore.OpenApi.OpenApiSchemaService.CreateSchema(Type type)
   at Microsoft.AspNetCore.OpenApi.OpenApiSchemaService.GetOrCreateUnresolvedSchemaAsync(OpenApiDocument document, Type type, IServiceProvider scopedServiceProvider, IOpenApiSchemaTransformer[] schemaTransformers, ApiParameterDescription parameterDescription, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.OpenApi.OpenApiSchemaService.GetOrCreateSchemaAsync(OpenApiDocument document, Type type, IServiceProvider scopedServiceProvider, IOpenApiSchemaTransformer[] schemaTransformers, ApiParameterDescription parameterDescription, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.OpenApi.OpenApiDocumentService.GetJsonRequestBody(OpenApiDocument document, IList`1 supportedRequestFormats, ApiParameterDescription bodyParameter, IServiceProvider scopedServiceProvider, IOpenApiSchemaTransformer[] schemaTransformers, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.OpenApi.OpenApiDocumentService.GetRequestBodyAsync(OpenApiDocument document, ApiDescription description, IServiceProvider scopedServiceProvider, IOpenApiSchemaTransformer[] schemaTransformers, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.OpenApi.OpenApiDocumentService.GetOperationAsync(ApiDescription description, OpenApiDocument document, IServiceProvider scopedServiceProvider, IOpenApiSchemaTransformer[] schemaTransformers, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.OpenApi.OpenApiDocumentService.GetOperationsAsync(IGrouping`2 descriptions, OpenApiDocument document, IServiceProvider scopedServiceProvider, IOpenApiOperationTransformer[] operationTransformers, IOpenApiSchemaTransformer[] schemaTransformers, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.OpenApi.OpenApiDocumentService.GetOpenApiPathsAsync(OpenApiDocument document, IServiceProvider scopedServiceProvider, IOpenApiOperationTransformer[] operationTransformers, IOpenApiSchemaTransformer[] schemaTransformers, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.OpenApi.OpenApiDocumentService.GetOpenApiDocumentAsync(IServiceProvider scopedServiceProvider, HttpRequest httpRequest, CancellationToken cancellationToken)
```

---

## Library version

`feat/synapse-endpoints` (`UnambitiousFx.Synapse.Endpoints.OpenApi`, unreleased)

## .NET version

.NET 10.0 (`Microsoft.AspNetCore.OpenApi` 10.0.11)

## Operating system

macOS (Darwin 25.6.0, arm64)

---

## Additional context

### Root cause

Traced through the real ASP.NET Core 10.0.11 source
(`src/Mvc/Mvc.ApiExplorer/src/EndpointMetadataApiDescriptionProvider.cs`,
`src/OpenApi/src/Services/OpenApiDocumentService.cs`, both fetched from the tagged `v10.0.11` source
tree): `EndpointMetadataApiDescriptionProvider.CreateApiDescription` builds
`ApiDescription.ParameterDescriptions` from the endpoint's `IParameterBindingMetadata` (derived from
the mapped delegate's actual C# parameters). Because a Synapse endpoint maps through one untyped
`HttpContext` delegate parameter (no method-level body parameter is ever inferred — the same fact
`SynapseParameterTransformer`'s own remarks document for route parameters), the provider finds no
body/form-sourced parameter (`hasBodyOrFormFileParameter == false`) but the endpoint still carries an
`IAcceptsMetadata`, so it unconditionally synthesizes one more:

```csharp
Type = acceptsRequestType ?? typeof(void)
```

Since `FormRequestMetadata.RequestType` is deliberately `null` (a message holding an `IFormFile` has
no JSON schema — a **prior task's** design decision, re-confirmed as still correct: the null design
is also required so the field-by-field schema uses the binder's real wire field names rather than
whatever JSON naming policy the framework's own type-based schema would apply), `Type` becomes
`typeof(void)`.

`ApiDescriptionExtensions.TryGetBodyParameter` then matches on `Source == BindingSource.Body` alone
(it does not check whether `Type` is something describable), so
`OpenApiDocumentService.GetRequestBodyAsync` routes to `GetJsonRequestBody`, which — because
`description.SupportedRequestFormats` is non-empty (populated from `acceptsMetadata.ContentTypes`,
regardless of what those content types actually are) — calls
`_componentService.GetOrCreateSchemaAsync(document, bodyParameter.Type, ...)` with
`bodyParameter.Type == typeof(void)`. `OpenApiSchemaService.CreateSchema` hands that straight to
`System.Text.Json.Schema.JsonSchemaExporter.GetJsonSchemaAsNode`, which throws for `System.Void`
unconditionally, regardless of the declared content types being form types rather than JSON. Nothing
in the call chain (`GetOperationAsync` → `GetOperationsAsync` → `GetOpenApiPathsAsync` →
`GetOpenApiDocumentAsync`) catches it, so one endpoint with this shape fails the whole document,
every endpoint included.

I verified this is a pure framework interaction, unrelated to any Synapse code, with the
from-scratch, zero-Synapse repro reproduced above. I also confirmed this could **not** be fixed by
changing `FormRequestMetadata.RequestType` to a non-null placeholder (e.g. `typeof(object)`):
`test/Synapse.Endpoints.Tests/OpenApiMetadataTests.cs` has two tests
(`CreateDescriptor_ForABinderReportingAFormBody_DeclaresBothFormContentTypesAndNoSchema` and
`FormRequestMetadata_WithFields_ExposesThemAndKeepsBothContentTypes`) that explicitly assert
`RequestType` is `null`, so that door is closed — correctly, for the field-naming reason above.

### Resolution

`FormRequestBodyDescriptionFixup` (`src/Synapse.Endpoints.OpenApi/Internal/FormRequestBodyDescriptionFixup.cs`)
is a second `IApiDescriptionProvider` (`Order = -1050`, run just after the framework's own
`EndpointMetadataApiDescriptionProvider` at `Order = -1100`, and unambiguously ahead of MVC's
`DefaultApiDescriptionProvider`) that walks `context.Results` in `OnProvidersExecuting` and deletes
exactly the synthetic `Source == BindingSource.Body && Type == typeof(void)` parameter for any
`ApiDescription` whose endpoint carries `FormRequestMetadata`. With that parameter gone,
`ApiDescription.TryGetBodyParameter` and `TryGetFormParameters` both report nothing, so
`GetRequestBodyAsync` returns `null` instead of throwing — leaving `operation.RequestBody` null,
which is exactly the state `SynapseParameterTransformer.ApplyFormSchema` expects to replace with the
real, populated form schema.

`services.AddSynapseEndpointsOpenApi()` registers both the fixup and the `SynapseParameterTransformer`
operation transformer in one call — they are inseparable: the workaround alone declares no
parameters, and the transformer alone crashes the document for any form endpoint, so an API letting
you register only one of the two would be unusable correctly on its own.

**The root cause is in the framework, not in Synapse.** This workaround can be deleted entirely if
ASP.NET Core stops synthesising a `typeof(void)` body parameter for an `IAcceptsMetadata` with a
`null RequestType` — a fix upstream would make `FormRequestBodyDescriptionFixup` dead code, not a
compatibility break.

**The workaround lives in the opt-in `UnambitiousFx.Synapse.Endpoints.OpenApi` package.** An
application that maps a form-bound endpoint and calls `AddOpenApi()` **without** referencing
`UnambitiousFx.Synapse.Endpoints.OpenApi` (and calling `AddSynapseEndpointsOpenApi()`) still has the
broken document described above — the core `Synapse.Endpoints` package carries no dependency on
`Microsoft.AspNetCore.OpenApi` and so cannot fix this on its own.

**Not yet reported upstream.** This has not been filed against `dotnet/aspnetcore` as of this
writing; the worked-around behavior here is a local mitigation, not a substitute for an upstream fix.

**Verification.** `test/Synapse.Endpoints.OpenApi.Tests/FormSchemaDocumentTests.cs` exercises the
full pipeline against a real `OpenApiDocumentService`: a form message, a form message with a single
`IFormFile`, a form message with an `IFormFile` collection, a form binder reporting zero described
fields, and a document mixing a form endpoint with a JSON endpoint in the same document — all pass,
producing a `200` document with both form content types populated and the JSON endpoint's request
body left untouched. Before the fixup existed, the same tests reproduced the exact stack trace
above.
