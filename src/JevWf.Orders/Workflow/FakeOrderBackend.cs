using JevWf.Orders.Models;

namespace JevWf.Orders.Workflow;

// In-memory implementation of the OrderToolCatalog actions, just to exercise the workflow
// end-to-end without a real ERP/logistics system behind it. Every gate that would need a real
// human/external system in production (seller confirmation, blocking-requirement validation,
// manual review) auto-approves here unless the item ID is explicitly listed as denied/rejected -
// that's what lets tests exercise the unhappy paths deterministically.
public sealed class FakeOrderBackend
{
    private readonly List<string> _log = new();
    private readonly IReadOnlySet<string> _deniedBlockingRequirementItemIds;
    private readonly IReadOnlySet<string> _manualReviewRejectedItemIds;

    public FakeOrderBackend(
        IReadOnlySet<string>? deniedBlockingRequirementItemIds = null,
        IReadOnlySet<string>? manualReviewRejectedItemIds = null)
    {
        _deniedBlockingRequirementItemIds = deniedBlockingRequirementItemIds ?? new HashSet<string>();
        _manualReviewRejectedItemIds = manualReviewRejectedItemIds ?? new HashSet<string>();
    }

    public IReadOnlyList<string> Log => _log;

    // ---- Intake / gating ----

    public void RequestSellerConfirmation(OrderItem item)
    {
        item.Status = ItemStatus.AwaitingSellerConfirmation;
        _log.Add($"request_seller_confirmation({item.ItemId}, seller={item.SellerId}) -> confirmed");
    }

    public void AwaitPaymentApproval(OrderItem item)
    {
        item.Status = ItemStatus.AwaitingPaymentApproval;
        _log.Add($"await_payment_approval({item.ItemId}, seller={item.SellerId})");
    }

    public bool RequestBlockingRequirementValidation(string itemId, string requirementType, string attachmentId)
    {
        var approved = !_deniedBlockingRequirementItemIds.Contains(itemId);
        _log.Add($"request_blocking_requirement_validation({itemId}, type={requirementType}, {attachmentId}) -> {(approved ? "approved" : "denied")}");
        return approved;
    }

    public bool EscalateForManualReview(string itemId, string reason)
    {
        var approved = !_manualReviewRejectedItemIds.Contains(itemId);
        _log.Add($"escalate_for_manual_review({itemId}, \"{reason}\") -> {(approved ? "approved" : "rejected")}");
        return approved;
    }

    // ---- Sourcing / fulfillment ----

    public void ReserveItemStock(string itemId, string originId, int quantity) =>
        _log.Add($"reserve_item_stock({itemId}, origin={originId}, qty={quantity})");

    public void SetShippingMethod(string itemId, string method) =>
        _log.Add($"set_shipping_method({itemId}, {method})");

    public void MarkItemDigitalOnly(string itemId) => _log.Add($"mark_item_digital_only({itemId})");

    public void StartFulfillment(string itemId) => _log.Add($"start_fulfillment({itemId})");

    public void HandleHandlingException(OrderItem item, string reason)
    {
        item.Status = ItemStatus.HandlingException;
        _log.Add($"handle_handling_exception({item.ItemId}, \"{reason}\")");
    }

    public void MarkItemShipped(OrderItem item)
    {
        item.Status = ItemStatus.Shipped;
        _log.Add($"mark_item_shipped({item.ItemId})");
    }

    public void MarkItemDelivered(OrderItem item)
    {
        item.Status = ItemStatus.Delivered;
        _log.Add($"mark_item_delivered({item.ItemId})");
    }

    // ---- Post-delivery ----

    public void RequestReturn(OrderItem item, string reason)
    {
        item.Status = ItemStatus.ReturnRequested;
        _log.Add($"request_return({item.ItemId}, \"{reason}\")");
    }

    public void MarkItemReturned(OrderItem item)
    {
        item.Status = ItemStatus.Returned;
        _log.Add($"mark_item_returned({item.ItemId})");
    }

    // Digital items only: delivery is instantaneous (email/download link), no physical steps.
    public void CompleteItem(OrderItem item)
    {
        item.Status = ItemStatus.Delivered;
        _log.Add($"complete_item({item.ItemId})");
    }

    public void DeferItem(OrderItem item, string reason)
    {
        item.Status = ItemStatus.Deferred;
        _log.Add($"defer_item({item.ItemId}, \"{reason}\")");
    }

    public void CancelItem(OrderItem item, string reason)
    {
        item.Status = ItemStatus.Cancelled;
        _log.Add($"cancel_item({item.ItemId}, \"{reason}\")");
    }

    // One shipment per (origin, shipping method) group - items from different origins or
    // with incompatible shipping methods can never share a shipment, even in the same order.
    // Sets every contained item's status to Invoiced (the workflow marks Shipped separately).
    public void CreateShipment(string orderId, string originId, string shippingMethod, IReadOnlyList<OrderItem> items)
    {
        foreach (var item in items)
            item.Status = ItemStatus.Invoiced;

        var itemIds = items.Select(i => i.ItemId).ToList();
        _log.Add($"create_shipment({orderId}, origin={originId}, method={shippingMethod}, items=[{string.Join(", ", itemIds)}])");
    }

    public void NotifyCustomer(string orderId, string message) =>
        _log.Add($"notify_customer({orderId}, \"{message}\")");

    public void RefundOrder(string orderId, string itemId, string reason) =>
        _log.Add($"refund_order({orderId}, item={itemId}, \"{reason}\")");

    public void CloseOrder(string orderId) => _log.Add($"close_order({orderId})");

    // ---- Jev decisions (not tools - recorded so the log shows who chose each tool) ----

    public void LogDecision(string itemId, IEnumerable<string> options, string chosen, double confidence) =>
        _log.Add($"[jev] {itemId}: options=[{string.Join(", ", options)}] -> {chosen} (confidence {confidence:F2})");

    public void LogRejected(string itemId, string tool, string reason) =>
        _log.Add($"[rejected] {tool}({itemId}): {reason}");
}
