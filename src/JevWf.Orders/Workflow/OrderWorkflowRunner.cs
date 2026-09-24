using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;

namespace JevWf.Orders.Workflow;

// Purely deterministic: reads each item's already-resolved attributes (BlockingRequirementType,
// IsDigitalOnly, ShippingMethod, ClassificationConfidence) and decides which OrderToolCatalog
// actions to trigger. Has no knowledge of the Jev or how those attributes were resolved - that
// happens upstream, in JevWf.Classification, before Start runs.
//
// Unlike a single synchronous Run, this is event-driven past the payment gate: Start takes every
// item up to AwaitingPaymentApproval (per seller - a marketplace order can have one seller's
// items proceed while another's are still clearing payment), then HandleEvent reacts to whatever
// arrives next (payment cleared/denied, a warehouse restocked, a carrier scan, a handling
// exception, a return request). Finalize does the one thing that only makes sense to evaluate
// across the whole order - grouping ready items into shipments - and can be called as many
// times as needed; it only ever touches items currently awaiting invoicing.
public sealed class OrderWorkflowRunner
{
    // Below this, classification's own answer isn't trusted enough to act on automatically.
    private const double ManualReviewConfidenceThreshold = 0.75;

    private static readonly HashSet<ItemStatus> TerminalStatuses = new()
    {
        ItemStatus.Delivered, ItemStatus.Cancelled, ItemStatus.Returned, ItemStatus.Refunded
    };

    private readonly FakeOrderBackend _backend;
    private readonly FakeInventoryCatalog _inventory;

    public OrderWorkflowRunner(FakeOrderBackend backend, FakeInventoryCatalog inventory)
    {
        _backend = backend;
        _inventory = inventory;
    }

    public void Start(Order order)
    {
        foreach (var item in order.Items)
        {
            _backend.RequestSellerConfirmation(item);
            _backend.AwaitPaymentApproval(item);
        }
    }

    public void HandleEvent(Order order, OrderEvent evt)
    {
        switch (evt)
        {
            case PaymentApprovedEvent approved:
                foreach (var item in order.Items.Where(i =>
                             i.SellerId == approved.SellerId && i.Status == ItemStatus.AwaitingPaymentApproval))
                {
                    AdvancePastPaymentGate(item);
                }
                break;

            case PaymentDeniedEvent denied:
                foreach (var item in order.Items.Where(i =>
                             i.SellerId == denied.SellerId && i.Status == ItemStatus.AwaitingPaymentApproval))
                {
                    _backend.CancelItem(item, "payment denied");
                }
                break;

            case RestockEvent restock:
                var deferredItem = order.Items.SingleOrDefault(i => i.ItemId == restock.ItemId);
                if (deferredItem is null || deferredItem.Status != ItemStatus.Deferred)
                    throw new InvalidOperationException($"Item {restock.ItemId} is not currently Deferred.");

                _inventory.Restock(restock.ItemId, restock.OriginId, restock.NewAvailableStock);
                RunSourcingAndFulfillment(deferredItem);
                break;

            case HandlingExceptionEvent handlingException:
                var handlingItem = order.Items.Single(i => i.ItemId == handlingException.ItemId);
                _backend.HandleHandlingException(handlingItem, handlingException.Reason);
                // The fake can't tell a recoverable problem (damaged - needs a fresh unit from
                // another origin) from a fatal one, so it always retries via re-sourcing.
                handlingItem.ChosenOriginId = null;
                _backend.DeferItem(handlingItem, $"handling exception: {handlingException.Reason}");
                break;

            case CarrierDeliveredEvent delivered:
                var shippedItem = order.Items.Single(i => i.ItemId == delivered.ItemId && i.Status == ItemStatus.Shipped);
                _backend.MarkItemDelivered(shippedItem);
                break;

            case ReturnRequestedEvent returnRequested:
                var deliveredItem = order.Items.Single(i => i.ItemId == returnRequested.ItemId && i.Status == ItemStatus.Delivered);
                _backend.RequestReturn(deliveredItem, returnRequested.Reason);
                _backend.MarkItemReturned(deliveredItem);
                _backend.RefundOrder(order.OrderId, deliveredItem.ItemId, returnRequested.Reason);
                break;

            case FinalizeEvent:
                Finalize(order);
                break;
        }
    }

    // Groups every item currently awaiting invoicing into shipments (by origin + shipping
    // method, never by seller), notifies the customer, and closes the order if every item has
    // reached a terminal state. Safe to call repeatedly - it only acts on items in
    // VerifyingInvoice, so items shipped by an earlier call are never touched again.
    public void Finalize(Order order)
    {
        var readyToShip = order.Items.Where(i => i.Status == ItemStatus.VerifyingInvoice).ToList();
        var shipmentGroups = readyToShip.GroupBy(i => (i.ChosenOriginId!, i.ShippingMethod!));

        foreach (var group in shipmentGroups)
        {
            var (originId, shippingMethod) = group.Key;
            var items = group.ToList();

            _backend.CreateShipment(order.OrderId, originId, shippingMethod, items);
            foreach (var item in items)
                _backend.MarkItemShipped(item);
        }

        var deferred = order.Items.Where(i => i.Status == ItemStatus.Deferred).Select(i => i.ItemId).ToList();
        if (deferred.Count > 0)
            _backend.NotifyCustomer(order.OrderId, $"Your order shipped partially. Item(s) {string.Join(", ", deferred)} are awaiting restock.");
        else if (readyToShip.Count > 0)
            _backend.NotifyCustomer(order.OrderId, "Your order has been confirmed and is on its way.");

        if (order.Items.All(i => TerminalStatuses.Contains(i.Status)))
            _backend.CloseOrder(order.OrderId);
    }

    private void AdvancePastPaymentGate(OrderItem item)
    {
        if (item.IsDigitalOnly is not bool isDigitalOnly ||
            item.BlockingRequirementType is not string blockingRequirementType ||
            item.ShippingMethod is not string shippingMethod ||
            item.ClassificationConfidence is not double confidence)
        {
            throw new InvalidOperationException($"Item {item.ItemId} has not been classified yet.");
        }

        if (isDigitalOnly)
        {
            _backend.MarkItemDigitalOnly(item.ItemId);
            _backend.CompleteItem(item);
            return;
        }

        if (blockingRequirementType != "none")
        {
            var validated = _backend.RequestBlockingRequirementValidation(item.ItemId, blockingRequirementType, $"attachment-{item.ItemId}");
            if (!validated)
            {
                _backend.CancelItem(item, $"{blockingRequirementType} not validated");
                return;
            }
        }

        if (confidence < ManualReviewConfidenceThreshold)
        {
            var approved = _backend.EscalateForManualReview(item.ItemId, $"classification confidence {confidence:F2} below threshold");
            if (!approved)
            {
                _backend.CancelItem(item, "manual review rejected");
                return;
            }
        }

        _backend.SetShippingMethod(item.ItemId, shippingMethod);
        RunSourcingAndFulfillment(item);
    }

    // Sourcing: pick the best-stocked, lowest-effective-lead-time origin. Purely numeric -
    // never a Jev call. No candidate with stock means the item is deferred until a RestockEvent.
    private void RunSourcingAndFulfillment(OrderItem item)
    {
        var candidates = _inventory.GetCandidates(item.ItemId);
        var chosen = SourcingResolver.Resolve(candidates);
        if (chosen is null)
        {
            _backend.DeferItem(item, "no sourcing origin with available stock");
            return;
        }

        item.ChosenOriginId = chosen.OriginId;
        _backend.ReserveItemStock(item.ItemId, chosen.OriginId, item.QuantityRequested);

        // The fake has no real cancellation-window timer or handling delay - it walks straight
        // through these stages. A real backend would pause at each one for its own trigger.
        item.Status = ItemStatus.AwaitingCancellationWindow;
        item.Status = ItemStatus.ReadyForHandling;
        _backend.StartFulfillment(item.ItemId);
        item.Status = ItemStatus.Handling;
        item.Status = ItemStatus.VerifyingInvoice;
    }
}
