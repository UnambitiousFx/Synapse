# MinimalApi.Modules.Contracts

Shared event contracts for the modular-monolith example.

## Purpose

This library is the **only thing** the Orders and Notifications modules share.  
Neither module references the other — both reference only this assembly.

```
Orders ──references──▶ Contracts
Notifications ─────▶ Contracts
(Orders ✗──────────▶ Notifications)
```

## Contents

| Type | Description |
|------|-------------|
| `OrderPlacedEvent` | Published by the Orders module when an order is placed. Consumed by the Notifications module (and any other future subscriber). |

## Dependencies

Only `UnambitiousFx.Synapse.Abstractions` (for `IEvent`). No handlers, no DI, no ASP.NET Core.

## See Also

- [`../MinimalApi.Modules.Orders`](../MinimalApi.Modules.Orders/README.md) — emitter (source generator)
- [`../MinimalApi.Modules.Notifications`](../MinimalApi.Modules.Notifications/README.md) — subscriber (manual registration)
