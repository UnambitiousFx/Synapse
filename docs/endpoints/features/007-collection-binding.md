# 007 — Collection binding from repeated keys

|  |  |
|---|---|
| **Status** | ✅ Shipped |
| **Priority** | Medium-High |
| **Area** | Binding / Generator |
| **Tiers** | All bound tiers |
| **Breaking** | No — additive |

## Problem

`?tag=a&tag=b&tag=c` cannot bind to a `string[] Tags` property. Filtering and multi-select query
parameters are routine, and the high level cannot express them at all.

Worse, the failure is loud in the wrong way: the property is rejected at compile time as unparsable
rather than reported as an unsupported shape.

## Current state

- `src/Synapse.Endpoints.Generator/Diagnostics/EndpointDiagnostics.cs` — `SYNE012` requires every
  bound property to be a `string`, an enum, or to expose `TryParse(string, out T)` /
  `TryParse(string, IFormatProvider, out T)`. `string[]` satisfies none of these, so a perfectly
  reasonable message is an error with a message about adding a `TryParse` overload to an array type.
- `src/Synapse.Endpoints/Binding/BindingHelpers.cs` — `TryGetQuery` takes `raw[0]` and discards the
  rest. Same in `TryGetHeader`.
- `HttpContextBindingExtensions.QueryValues` returns all values, but only for hand-written handlers;
  no generated binder and no `BindingValidator` method uses it.

## What you cannot write today

`GET /tasks/search?tag=docs&tag=ship&status=Open` — a filter with two repeated keys:

```csharp
public sealed record SearchTasksQuery : IRequest<IReadOnlyList<TaskDto>>
{
    [FromQuery(Name = "tag")] public string[] Tags { get; init; } = [];
    [FromQuery(Name = "status")] public List<TaskStatus> Statuses { get; init; } = [];
}
```

Neither property compiles:

```
error SYNE012: Property 'Tags' on 'SearchTasksQuery' has type 'string[]', which is not string,
  not an enum, and has no public static TryParse(string, out string[]) or
  TryParse(string, IFormatProvider, out string[]) method, so its bound value cannot be parsed.
  Add either overload to 'string[]' — implementing IParsable<string[]> supplies the second — or
  change the type of 'Tags'.
```

The advice is unfollowable: `string[]` is not a type anyone can add a `TryParse` to, and the shape
being rejected is not unparsable — it is unsupported. So the endpoint drops to a hand-written binder
(this is the `TagReportEndpoint` example in `docs/docs/endpoints/low-level/validating.mdx`, which
exists precisely because the high level cannot express it):

```csharp
[Get("/tasks/search")]
public sealed class SearchTasksEndpoint : RawEndpoint<SearchTasksQuery, IReadOnlyList<TaskDto>>
{
    public override ValueTask<BindResult<SearchTasksQuery>> BindAsync(HttpContext context)
    {
        var v = context.Validate();

        // QueryValues exists, but nothing on the collector consumes it — so the parse loop, the
        // per-element error and the empty-vs-absent decision are all written by hand, per endpoint.
        var tags = context.QueryValues("tag")
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag!)
            .ToArray();

        var statuses = new List<TaskStatus>();
        foreach (var raw in context.QueryValues("status"))
        {
            if (Enum.TryParse<TaskStatus>(raw, ignoreCase: true, out var parsed))
            {
                statuses.Add(parsed);
            }
            else
            {
                v.AddError("status", $"'{raw}' is not a valid TaskStatus.");
            }
        }

        return ValueTask.FromResult(v.IsValid
            ? BindResult<SearchTasksQuery>.Success(new SearchTasksQuery { Tags = tags, Statuses = statuses })
            : BindResult<SearchTasksQuery>.Failure(v));
    }
}
```

Twenty lines, and the OpenAPI document still does not declare either parameter as an array.

## Proposed API

Generator: recognise `T[]`, `List<T>`, `IReadOnlyList<T>` and `IEnumerable<T>` where the *element*
type satisfies the existing `SYNE012` parseability rule, and emit a loop over `QueryValues`.

Validator additions:

```csharp
public bool QueryValues<T>(string name, out T[] values) where T : IParsable<T>;
public bool HeaderValues<T>(string name, out T[] values) where T : IParsable<T>;
```

Semantics to pin down and document:

- Absent key → empty; nullable → `null`. A non-nullable collection binds to an empty array/list and
  reports nothing when the key is absent — HTTP cannot express zero values under a key, so demanding
  presence would ask for something unsendable. A **nullable** collection (`string[]?`) binds `null`
  instead, which is how "absent" and "empty" stay distinguishable.
- One unparsable element → one error naming the index, and binding continues so the rest still
  accumulate.
- Comma-separated single values (`?tag=a,b`) are **not** split. Repeated keys only. Splitting is a
  convention, and picking one silently is how you get a bug report about a tag containing a comma.

### With the proposal

The message from the top of this file, unchanged, with no `BindAsync` at all:

```csharp
public sealed record SearchTasksQuery : IRequest<IReadOnlyList<TaskDto>>
{
    [FromQuery(Name = "tag")] public string[] Tags { get; init; } = [];
    [FromQuery(Name = "status")] public List<TaskStatus> Statuses { get; init; } = [];
}

[Get("/tasks/search")]
public sealed class SearchTasksEndpoint : Endpoint<SearchTasksQuery, IReadOnlyList<TaskDto>>;
```

```bash
$ curl -s 'localhost:5000/tasks/search?tag=docs&tag=ship&status=Open'
# Tags = ["docs","ship"], Statuses = [Open]

$ curl -s 'localhost:5000/tasks/search?tag=docs'
# Tags = ["docs"], Statuses = [] — absent optional collection, not an error

$ curl -s 'localhost:5000/tasks/search?status=Open&status=Nope'
{"status":400,"errors":{"status":["The query value at index 1 is not a valid TaskStatus."]}}
```

Note the last one: index `0` still bound, so a request with several bad elements reports all of
them. And `?tag=a,b` is one tag named `a,b` — repeated keys only, no silent splitting.

At the low level, the same loop becomes one call — named `QueryValuesEnum`, not `QueryValues`,
because an enum cannot satisfy `where T : IParsable<T>` (the same reason `QueryEnum` sits beside
`Query<T>`):

```csharp
var v = context.Validate();
v.QueryValuesEnum<TaskStatus>("status", out var statuses);   // per-element errors, accumulated
```

## Acceptance criteria

- [x] `SYNE012` reports the element type, not the collection type, when the element is unparsable.
- [x] Empty vs absent is distinguishable and documented — a non-nullable collection is empty and
      silent, a nullable one is `null`. See [Repeated keys](../../docs/endpoints/high-level/messages.mdx#repeated-keys).
- [x] OpenAPI declares an array parameter with the right element schema — shipped as feature 018
      (OpenAPI parameter metadata), via the opt-in `UnambitiousFx.Synapse.Endpoints.OpenApi`
      package. See [Declaring query, header and route parameters](../../docs/endpoints/reference/openapi.mdx#declaring-query-header-and-route-parameters).
- [x] Tests in `test/Synapse.Endpoints.Generator.Tests/CollectionBinderEmissionTests.cs` and
      `CollectionDiagnosticTests.cs`.
