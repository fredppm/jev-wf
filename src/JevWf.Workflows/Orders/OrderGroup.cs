namespace JevWf.Workflows.Orders;

// What the customer bought. Each order belongs to one seller and gets its own workflow.
public sealed record OrderGroup(string OrderGroupId, IReadOnlyList<Order> Orders);

public sealed record Order(string OrderId, string SellerId, IReadOnlyList<OrderItem> Items)
{
    public decimal Total => Items.Sum(item => item.Total);
}

public sealed record OrderItem(string ItemId, string ProductName, string Description, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}
