# MinimalApi.Modules.Notifications

Notifications module for the modular-monolith example — uses **manual Synapse registration** (no source generator).

## What It Shows

- Event handler (`OrderPlacedNotificationHandler`) registered via `cfg.RegisterEventHandler<THandler, TEvent>()`
- No `[EventHandler<T>]` attribute, no generated `RegisterGroup`
- One call registers both the DI service and the AOT-safe event dispatch delegate
- Module exposes `AddNotificationsHandlers(ISynapseConfig)` so the host calls it inside `AddSynapse(...)`
- `NotificationLog` singleton accumulates received notifications; `GET /notifications` exposes them

## Registration Path

```csharp
// In Program.cs (host)
builder.Services.AddNotificationsModule();  // registers NotificationLog singleton

builder.Services.AddSynapse(cfg =>
{
    // Manual path — explicit alternative to cfg.AddRegisterGroup(new RegisterGroup())
    // RegisterEventHandler wires the DI service AND the dispatch delegate in one call.
    cfg.AddNotificationsHandlers();
    //  ↳ internally: cfg.RegisterEventHandler<OrderPlacedNotificationHandler, OrderPlacedEvent>()
});

app.MapNotificationsEndpoints();
```

## Project Structure

```
MinimalApi.Modules.Notifications/
├── Handlers/
│   └── OrderPlacedNotificationHandler.cs  # IEventHandler<OrderPlacedEvent> — NO [EventHandler<T>]
├── NotificationLog.cs    # Thread-safe log + NotificationEntry record
└── NotificationsModule.cs  # AddNotificationsModule() + AddNotificationsHandlers() + MapNotificationsEndpoints()
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/notifications` | List all notification entries recorded since startup |

## Dependencies

- `Synapse.AspNetCore` + `Synapse` — handler interfaces + `ISynapseConfig`
- `MinimalApi.Modules.Contracts` — `OrderPlacedEvent` shared contract
- **No** `Synapse.Generator` reference — manual registration only
- **No reference to** `MinimalApi.Modules.Orders`

## Contrast

Compare with [`../MinimalApi.Modules.Orders`](../MinimalApi.Modules.Orders/README.md) which uses
`[RequestHandler<T>]` + the source generator.
