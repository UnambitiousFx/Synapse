# ValidateSynapse / ValidateOnStart — Design

Issue: #102 (sub-issue of #96). Builds on `IPipelineDescriber` (#101).

## Goal

Catch Synapse configuration mistakes at host start or in a test, instead of at the first request: a behavior
registered for a request that has no handler, two handlers for one request (all but the last registered are silently ignored today),
a pipeline that cannot be resolved, and `Order` ties.

## Public API (`Synapse.Abstractions`, `UnambitiousFx.Synapse.Abstractions`)

```csharp
public static class SynapseValidationExtensions
{
    public static SynapseValidationReport ValidateSynapse(this IServiceProvider services);
}

public sealed record SynapseValidationIssue(string Code, SynapseValidationSeverity Severity, string Message, Type Type);
public enum SynapseValidationSeverity { Warning, Error }

public sealed class SynapseValidationReport
{
    public IReadOnlyList<SynapseValidationIssue> Issues { get; }
    public IReadOnlyList<SynapseValidationIssue> Errors { get; }
    public IReadOnlyList<SynapseValidationIssue> Warnings { get; }
    public bool IsValid { get; }              // no errors; warnings do not count
    public void ThrowIfInvalid();             // throws SynapseValidationException when !IsValid
}

public sealed class SynapseValidationException : Exception { public SynapseValidationReport Report { get; } }
```

`ISynapseConfig.ValidateOnStart()` (in `Synapse`) registers a hosted service. On `StartAsync` it validates,
logs every warning, and throws `SynapseValidationException` if there are errors, so the host fails to start.
`ValidateSynapse()` never throws; the caller decides (`ThrowIfInvalid()` or assert on the report).

## Checks

Applies to requests (void and with response) and events. Stream requests are out of scope (the describer does not
cover them yet).

| Code | Severity | Rule |
|---|---|---|
| SYN001 | Error | A closed pipeline behavior is registered for a request/event type that has no handler. |
| SYN002 | Error | Two or more handlers are registered for one request type (the container resolves the last registered handler; the others are silently ignored). Events are exempt: several handlers per event is normal. |
| SYN003 | Error | The pipeline of a registered handler/event fails to resolve (missing dependency, throwing constructor). The exception message is included. |
| SYN004 | Warning | Two behaviors in one pipeline share an `Order`. Legal; they keep registration order. |

## Design

AOT rule: no assembly scanning, no `MakeGenericType`/`MakeGenericMethod`. Everything comes from closed generics known
at registration time.

- **Descriptor snapshot (SYN001, SYN002).** At the end of `AddSynapse`, an internal `SynapseRegistry` singleton
  captures, from the `IServiceCollection`, the request/event types of every `IRequestHandler<>`,
  `IRequestHandler<,>` and `IEventHandler<>` descriptor (with counts) and of every closed
  `IRequestPipelineBehavior<>`, `IRequestPipelineBehavior<,>` and `IEventPipelineBehavior<>` descriptor. Only
  `Type.GetGenericArguments()` on already-closed types is used. Open-generic behavior descriptors are skipped: DI
  closes them lazily over any request, so "applies to nothing" is not decidable at runtime.
- **Pipeline probes (SYN003, SYN004).** `RegisterRequestHandler<,>`, `RegisterRequestHandler<,,>` and
  `RegisterEventHandler<,>` (in both `SynapseConfig` and `DefaultDependencyInjectionBuilder`, which generated
  `RegisterGroup`s use) also record a probe: `(Type, Func<IPipelineDescriber, PipelineDescription?>)` such as
  `d => d.Describe<TRequest, TResponse>()`. This keeps the describer's generic entry points as the only path, so it
  stays AOT-safe. Probes are keyed by request/event type (first probe kept; they describe the same pipeline anyway).
- **Validator.** Internal `SynapseValidator` runs the four checks over the registry and probes. It calls
  `IPipelineDescriber` (singleton, already registered), wraps each probe in try/catch for SYN003, and reports SYN004
  from adjacent equal `Order` values in `PipelineDescription.Behaviors`.
- Codes are stable and documented; messages name the type and the fix.

## Out of scope / known limits

- A request type that has neither a handler nor a behavior is invisible at runtime; needs the Roslyn analyzer (#103).
- `[assembly: SynapseGlobalBehavior]` with the host's generated group never registered leaves nothing to inspect at
  runtime, so that #91 symptom is **not** detectable here. #91 stays open for #103. (Deviation from the earlier
  scoping answer; also dropped the planned "open-generic closed over nothing" warning for the reason above.)
- Stream requests.
- Behaviors added to the `IServiceCollection` after `AddSynapse` returns are not in the snapshot.

## Testing

- `SynapseValidatorTests`: one test per code (positive and negative), a clean configuration is valid, warnings do not
  make `IsValid` false, `ThrowIfInvalid` throws with the report attached, a generated-group-style registration
  (via `AddRegisterGroup`) is covered.
- `ValidateOnStart` hosted-service tests: throws on errors, logs warnings and starts on warnings only, not
  registered unless `ValidateOnStart()` is called.
- MinimalApi calls `ValidateOnStart()`; the existing Native AOT CI job proves it starts under AOT.

## Docs and changelog

New "Validating the configuration" section in `docs/docs/pipelines.mdx` (codes table, test snippet, startup snippet,
limits). No known-issue entry: this is a feature, not a bug fix.
