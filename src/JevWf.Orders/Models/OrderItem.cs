namespace JevWf.Orders.Models;

// There's no structured catalog/ERP yet: BlockingRequirementType, IsDigitalOnly, ShippingMethod
// and ClassificationConfidence start unresolved and must be filled in (e.g. by
// JevWf.Classification, which infers them from ProductName/Description) before
// OrderWorkflowRunner can run.
public sealed class OrderItem
{
    public required string ItemId { get; init; }
    public required string SellerId { get; init; }
    public required string ProductName { get; init; }
    public required string Description { get; init; }
    public required int QuantityRequested { get; init; }
    public decimal? UnitPrice { get; init; }
    public ItemStatus Status { get; set; } = ItemStatus.Pending;

    // "none" or a specific blocking requirement type (e.g. "prescription", "age_restricted").
    // Generic on purpose - a dedicated bool per case (like the old RequiresPrescription) doesn't
    // scale to age verification, export licenses, etc.
    public string? BlockingRequirementType { get; set; }
    public bool? IsDigitalOnly { get; set; }
    public string? ShippingMethod { get; set; }

    // Weakest-link confidence across this item's classification questions (0-1). Drives
    // escalation to AwaitingManualReview when below OrderWorkflowRunner's threshold.
    public double? ClassificationConfidence { get; set; }

    // Filled in by OrderWorkflowRunner via Sourcing.SourcingResolver. Null until a stocked
    // origin is found for this item (physical items only - digital items never get one).
    public string? ChosenOriginId { get; set; }
}
