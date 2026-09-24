namespace JevWf.Orders.Models;

// There's no structured catalog/ERP yet: the Jev infers is_digital,
// requires_prescription etc. from the free-text name/description (see OrderWorkflowRunner).
public sealed class OrderItem
{
    public required string ItemId { get; init; }
    public required string ProductName { get; init; }
    public required string Description { get; init; }
    public required int QuantityRequested { get; init; }
    public ItemStatus Status { get; set; } = ItemStatus.Pending;
}
