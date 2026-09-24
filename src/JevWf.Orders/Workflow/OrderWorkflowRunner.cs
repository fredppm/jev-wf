using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;

namespace JevWf.Orders.Workflow;

// Purely deterministic: reads each item's already-resolved attributes (RequiresPrescription,
// IsDigitalOnly, ShippingMethod) and decides which OrderToolCatalog actions to trigger.
// Has no knowledge of the Jev or how those attributes were resolved - that happens upstream,
// in JevWf.Classification, before this runs.
public sealed class OrderWorkflowRunner
{
    private readonly FakeOrderBackend _backend;
    private readonly FakeInventoryCatalog _inventory;

    public OrderWorkflowRunner(FakeOrderBackend backend, FakeInventoryCatalog inventory)
    {
        _backend = backend;
        _inventory = inventory;
    }

    public void Run(Order order)
    {
        var readyToShip = new List<OrderItem>();
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

            // Sourcing: pick the best-stocked, lowest-effective-lead-time origin. Purely
            // numeric - never a Jev call. No candidate with stock means the item is deferred.
            var candidates = _inventory.GetCandidates(item.ItemId);
            var chosen = SourcingResolver.Resolve(candidates);
            if (chosen is null)
            {
                _backend.DeferItem(item, "no sourcing origin with available stock");
                deferred.Add(item.ItemId);
                continue;
            }

            item.ChosenOriginId = chosen.OriginId;
            _backend.ReserveItemStock(item.ItemId, chosen.OriginId, item.QuantityRequested);
            _backend.StartFulfillment(item.ItemId);
            readyToShip.Add(item);
        }

        // Items only share a shipment if they ship from the same physical origin with a
        // compatible SLA - same order, same seller even, doesn't matter otherwise.
        var shipmentGroups = readyToShip.GroupBy(i => (i.ChosenOriginId!, i.ShippingMethod!));
        foreach (var group in shipmentGroups)
        {
            var (originId, shippingMethod) = group.Key;
            var itemIds = group.Select(i => i.ItemId).ToList();

            _backend.CreateShipment(order.OrderId, originId, shippingMethod, itemIds);
            foreach (var item in group)
                _backend.CompleteItem(item);
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
