using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;

public sealed record CompleteFulfillmentCommand : IRequest
{
    public required Guid FulfillmentId { get; init; }
}