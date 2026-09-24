namespace JevWf.Orders.Models;

public sealed class Order
{
    public required string OrderId { get; init; }
    public required string CustomerEmail { get; init; }
    public required string ShippingAddress { get; init; }
    public required List<OrderItem> Items { get; init; }
}
