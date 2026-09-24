using JevWf.Orders.Models;

namespace JevWf.Orders.Workflow;

// Purely deterministic: reads each item's already-resolved attributes (RequiresPrescription,
// IsDigitalOnly, ShippingMethod) and decides which OrderToolCatalog actions to trigger.
// Has no knowledge of the Jev or how those attributes were resolved — that happens upstream,
// in JevWf.Classification, before this runs.
public sealed class OrderWorkflowRunner
{
    private readonly FakeOrderBackend _backend;

    public OrderWorkflowRunner(FakeOrderBackend backend)
    {
        _backend = backend;
    }

    public void Run(Order order)
    {
        var shippedNow = new List<string>();
        var deferred = new List<string>();

        foreach (var item in order.Items)
        {
            if (item.IsDigitalOnly is not bool isDigitalOnly ||
                item.RequiresPrescription is not bool requiresPrescription ||
                item.ShippingMethod is not string shippingMethod)
            {
                throw new InvalidOperationException($"Item {item.ItemId} has not been classified yet.");
            }

            if (isDigitalOnly)
            {
                _backend.MarkItemDigitalOnly(item.ItemId);
                _backend.CompleteItem(item);
                continue;
            }

            if (requiresPrescription)
            {
                var approved = _backend.RequestPrescriptionValidation(item.ItemId, $"attachment-{item.ItemId}");
                if (!approved)
                {
                    _backend.CancelItem(item, "prescription not validated");
                    continue;
                }
            }

            _backend.SetShippingMethod(item.ItemId, shippingMethod);

            var available = _backend.CheckItemStock(item.ItemId);
            if (available >= item.QuantityRequested)
            {
                _backend.ReserveItemStock(item.ItemId, item.QuantityRequested);
                _backend.StartFulfillment(item.ItemId);
                shippedNow.Add(item.ItemId);
            }
            else
            {
                _backend.DeferItem(item, "insufficient stock");
                deferred.Add(item.ItemId);
            }
        }

        if (shippedNow.Count > 0)
        {
            _backend.CreatePartialShipment(order.OrderId, shippedNow);

            foreach (var itemId in shippedNow)
            {
                var item = order.Items.First(i => i.ItemId == itemId);
                if (item.Status != ItemStatus.Delivered)
                    _backend.CompleteItem(item);
            }
        }

        if (deferred.Count > 0)
        {
            _backend.NotifyCustomer(
                order.OrderId,
                $"Your order shipped partially. Item(s) {string.Join(", ", deferred)} are awaiting restock.");
        }
        else
        {
            _backend.NotifyCustomer(order.OrderId, "Your order has been confirmed and is on its way.");
        }

        var allItemsTerminal = order.Items.All(i => i.Status is ItemStatus.Delivered or ItemStatus.Cancelled);
        if (allItemsTerminal)
            _backend.CloseOrder(order.OrderId);
    }
}
