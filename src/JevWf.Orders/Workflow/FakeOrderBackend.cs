using JevWf.Orders.Models;

namespace JevWf.Orders.Workflow;

// In-memory implementation of the OrderToolCatalog actions, just to exercise the workflow
// end-to-end without a real ERP/stock system behind it.
public sealed class FakeOrderBackend
{
    private readonly Dictionary<string, int> _stockByItemId;
    private readonly List<string> _log = new();

    public FakeOrderBackend(Dictionary<string, int> stockByItemId)
    {
        _stockByItemId = stockByItemId;
    }

    public IReadOnlyList<string> Log => _log;

    public int CheckItemStock(string itemId)
    {
        var available = _stockByItemId.GetValueOrDefault(itemId, 0);
        _log.Add($"check_item_stock({itemId}) -> {available} available");
        return available;
    }

    public void ReserveItemStock(string itemId, int quantity)
    {
        _stockByItemId[itemId] = _stockByItemId.GetValueOrDefault(itemId, 0) - quantity;
        _log.Add($"reserve_item_stock({itemId}, {quantity})");
    }

    public void StartFulfillment(string itemId) => _log.Add($"start_fulfillment({itemId})");

    // Simulates prescription validation: always approves in this fake version.
    public bool RequestPrescriptionValidation(string itemId, string attachmentId)
    {
        _log.Add($"request_prescription_validation({itemId}, {attachmentId}) -> approved");
        return true;
    }

    public void SetShippingMethod(string itemId, string method) =>
        _log.Add($"set_shipping_method({itemId}, {method})");

    public void MarkItemDigitalOnly(string itemId) => _log.Add($"mark_item_digital_only({itemId})");

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

    public void CreatePartialShipment(string orderId, IReadOnlyList<string> itemIds) =>
        _log.Add($"create_partial_shipment({orderId}, [{string.Join(", ", itemIds)}])");

    public void NotifyCustomer(string orderId, string message) =>
        _log.Add($"notify_customer({orderId}, \"{message}\")");

    public void CloseOrder(string orderId) => _log.Add($"close_order({orderId})");
}
