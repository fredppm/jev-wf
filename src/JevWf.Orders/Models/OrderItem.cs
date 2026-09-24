namespace JevWf.Orders.Models;

// There's no structured catalog/ERP yet: RequiresPrescription, IsDigitalOnly and
// ShippingMethod start unresolved and must be filled in (e.g. by JevWf.Classification,
// which infers them from ProductName/Description) before OrderWorkflowRunner can run.
public sealed class OrderItem
{
    public required string ItemId { get; init; }
    public required string ProductName { get; init; }
    public required string Description { get; init; }
    public required int QuantityRequested { get; init; }
    public ItemStatus Status { get; set; } = ItemStatus.Pending;

    public bool? RequiresPrescription { get; set; }
    public bool? IsDigitalOnly { get; set; }
    public string? ShippingMethod { get; set; }
}
