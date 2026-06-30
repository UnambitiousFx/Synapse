# MinimalApi.Modules.Orders

Orders module for the modular-monolith example — uses the **Synapse source generator**.

## What It Shows

- Handler (`PlaceOrderCommandHandler`) registered via `[RequestHandler<TRequest, TResponse>]`
- Source generator emits a `RegisterGroup` class with all registrations
- Host wires it with one call: `cfg.AddRegisterGroup(new RegisterGroup())`
- `IEmitter.EmitAsync(new OrderPlacedEvent {...})` publishes the event without knowing who handles it

## Registration Path

```csharp
// In Program.cs (host)
builder.Services.AddOrdersModule();        // registers OrderStore singleton

builder.Services.AddSynapse(cfg =>
{
    // Source-gen path: generator discovers [RequestHandler<PlaceOrderCommand, Guid>]
    // and emits closed registrations in RegisterGroup.g.cs (visible in obj/)
    cfg.AddRegisterGroup(new global::UnambitiousFx.Examples.MinimalApi.Modules.Orders.RegisterGroup());
});

app.MapOrdersEndpoints();
```

## Project Structure

```
MinimalApi.Modules.Orders/
├── Messages/
│   └── PlaceOrderCommand.cs         # IRequest<Guid>
├── Handlers/
│   └── PlaceOrderCommandHandler.cs  # [RequestHandler<PlaceOrderCommand, Guid>] — source-gen
├── Behaviors/
│   └── OrderTracingBehavior.cs      # [PipelineBehavior] Order=15, closed over Guid (value type)
├── OrderStore.cs                    # Thread-safe in-memory store (singleton)
└── OrdersModule.cs                  # AddOrdersModule() + MapOrdersEndpoints()
```

`OrderTracingBehavior<TRequest, TResponse>` is an open-generic `[PipelineBehavior]` — the source generator cross-products it with `PlaceOrderCommand → Guid` and emits a **closed** registration safe under Native AOT, even though `Guid` is a value type.

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/orders` | Place an order — handler emits `OrderPlacedEvent` |

**Request body:**
```json
{ "product": "Widget", "quantity": 3 }
```

## Dependencies

- `Synapse.AspNetCore` + `Synapse` — handler + invoker
- `Synapse.Generator` (Analyzer) — source generator
- `MinimalApi.Modules.Contracts` — `OrderPlacedEvent` shared contract
- **No reference to** `MinimalApi.Modules.Notifications`

## Contrast

Compare with [`../MinimalApi.Modules.Notifications`](../MinimalApi.Modules.Notifications/README.md) which registers
its event handler **manually** without the source generator.
