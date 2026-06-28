using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Orders.Messages;

/// <summary>Command to place a new order. Returns the generated order ID.</summary>
public sealed record PlaceOrderCommand(string Product, int Quantity) : IRequest<Guid>;
