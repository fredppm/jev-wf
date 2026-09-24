using System.Globalization;
using JevWf.Classification;
using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;

namespace JevWf.Orders.Workflow;

// Event-driven order workflow where the Jev picks the tool at each item-level decision point.
//
// Split of responsibilities:
// - The workflow decides WHEN a decision is needed (payment approved, restock, handling
//   exception), WHICH tools are legal at that point, and the facts the Jev sees.
// - The Jev (IToolSelector) picks ONE tool from the legal ones.
// - The workflow executes it. Some tools produce new facts (e.g. requirement validation
//   result), so the loop asks again until the item reaches a waiting/terminal state.
//
// Steps with no real alternative stay deterministic: seller confirmation/payment gate (Start),
// payment denial, shipment grouping (Finalize - pure math), carrier delivery and returns.
public sealed class OrderWorkflowRunner
{
    private const int MaxDecisionSteps = 5;

    // Shown to the Jev as a policy hint next to the classification confidence.
    private const double ManualReviewConfidenceThreshold = 0.75;

    private static readonly HashSet<ItemStatus> TerminalStatuses = new()
    {
        ItemStatus.Delivered, ItemStatus.Cancelled, ItemStatus.Returned, ItemStatus.Refunded
    };

    // Written as the CONDITION under which each tool is the right one (like classification
    // criteria), not as what the tool does - the Jev matches the facts against these. The
    // action-style wording made the Jev pick defer_item almost every time.
    private static readonly Dictionary<string, string> ToolDescriptions = new()
    {
        ["mark_item_digital_only"] = "classified_digital_only is 'yes': the product is delivered electronically, nothing to ship.",
        ["request_blocking_requirement_validation"] = "classified_blocking_requirement is not 'none' and requirement_validation is 'not requested'.",
        ["escalate_for_manual_review"] = "classification_confidence is below the manual review threshold and manual_review is 'not requested'.",
        ["reserve_item_stock"] = "The product is physical (classified_digital_only is 'no'), stock says 'available at ...', and no requirement is pending or denied.",
        ["defer_item"] = "The product is physical and stock says 'no origin has stock'.",
        ["cancel_item"] = "requirement_validation is 'denied' or manual_review is 'rejected'."
    };

    private readonly FakeOrderBackend _backend;
    private readonly FakeInventoryCatalog _inventory;
    private readonly IToolSelector _selector;
    private readonly Dictionary<string, ItemDecisionState> _states = new();

    public OrderWorkflowRunner(FakeOrderBackend backend, FakeInventoryCatalog inventory, IToolSelector selector)
    {
        _backend = backend;
        _inventory = inventory;
        _selector = selector;
    }

    public void Start(Order order)
    {
        foreach (var item in order.Items)
        {
            _backend.RequestSellerConfirmation(item);
            _backend.AwaitPaymentApproval(item);
        }
    }

    public async Task HandleEventAsync(Order order, OrderEvent evt, CancellationToken ct = default)
    {
        switch (evt)
        {
            case PaymentApprovedEvent approved:
                foreach (var item in order.Items.Where(i =>
                             i.SellerId == approved.SellerId && i.Status == ItemStatus.AwaitingPaymentApproval).ToList())
                {
                    State(item).Trigger = $"payment approved by {approved.SellerId}";
                    await DecideAsync(item, ct);
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
                {
                    _backend.LogRejected(restock.ItemId, "RestockEvent", $"item is {deferredItem?.Status.ToString() ?? "unknown"}, expected Deferred");
                    break;
                }

                _inventory.Restock(restock.ItemId, restock.OriginId, restock.NewAvailableStock);
                State(deferredItem).Trigger = $"restock at {restock.OriginId} (new stock {restock.NewAvailableStock})";
                await DecideAsync(deferredItem, ct);
                break;

            case HandlingExceptionEvent handlingException:
                var handlingItem = order.Items.Single(i => i.ItemId == handlingException.ItemId);
                _backend.HandleHandlingException(handlingItem, handlingException.Reason);
                // The origin that failed is excluded from any further sourcing for this item.
                var state = State(handlingItem);
                if (handlingItem.ChosenOriginId is not null)
                    state.ExcludedOrigins.Add(handlingItem.ChosenOriginId);
                handlingItem.ChosenOriginId = null;
                state.Trigger = $"handling exception: {handlingException.Reason}";
                await DecideAsync(handlingItem, ct);
                break;

            case CarrierDeliveredEvent delivered:
                var shippedItem = order.Items.Single(i => i.ItemId == delivered.ItemId);
                if (shippedItem.Status != ItemStatus.Shipped)
                {
                    _backend.LogRejected(shippedItem.ItemId, "CarrierDeliveredEvent", $"item is {shippedItem.Status}, expected Shipped");
                    break;
                }
                _backend.MarkItemDelivered(shippedItem);
                break;

            case ReturnRequestedEvent returnRequested:
                var deliveredItem = order.Items.Single(i => i.ItemId == returnRequested.ItemId);
                if (deliveredItem.Status != ItemStatus.Delivered)
                {
                    _backend.LogRejected(deliveredItem.ItemId, "ReturnRequestedEvent", $"item is {deliveredItem.Status}, expected Delivered");
                    break;
                }
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

    // Ask the Jev -> execute -> repeat, until the chosen tool leaves the item waiting or done.
    private async Task DecideAsync(OrderItem item, CancellationToken ct)
    {
        EnsureClassified(item);
        var state = State(item);

        for (var step = 0; step < MaxDecisionSteps; step++)
        {
            var allowed = AllowedTools(item, state);
            var decision = await _selector.ChooseAsync(
                new ToolDecisionRequest(item.ItemId, item.ProductName, item.Description, BuildFacts(item, state), allowed), ct);

            _backend.LogDecision(item.ItemId, allowed.Keys, decision.Tool, decision.Confidence);

            if (Execute(item, state, decision.Tool))
                return;
        }

        _backend.LogRejected(item.ItemId, "decision_loop", $"no final action after {MaxDecisionSteps} decisions");
    }

    // Guardrails live here, not in the Jev: a tool is only offered when it's legal right now.
    private static Dictionary<string, string> AllowedTools(OrderItem item, ItemDecisionState state)
    {
        var tools = new List<string>();

        if (item.Status == ItemStatus.AwaitingPaymentApproval)
        {
            tools.Add("mark_item_digital_only");
            if (state.RequirementValidation is null)
                tools.Add("request_blocking_requirement_validation");
            if (state.ManualReview is null)
                tools.Add("escalate_for_manual_review");
        }

        // Never ship something with a legal requirement that hasn't been validated.
        var requirementBlocks = item.BlockingRequirementType != "none" && state.RequirementValidation != "approved";
        if (!requirementBlocks)
            tools.Add("reserve_item_stock");

        tools.Add("defer_item");
        tools.Add("cancel_item");

        return tools.ToDictionary(t => t, t => ToolDescriptions[t]);
    }

    private Dictionary<string, string> BuildFacts(OrderItem item, ItemDecisionState state)
    {
        var best = BestOrigin(item, state);

        var facts = new Dictionary<string, string>
        {
            ["trigger"] = state.Trigger,
            ["current_status"] = item.Status.ToString(),
            ["classified_blocking_requirement"] = item.BlockingRequirementType!,
            ["classified_digital_only"] = item.IsDigitalOnly!.Value ? "yes" : "no",
            ["classified_shipping_sla"] = item.ShippingMethod!,
            ["classification_confidence"] =
                $"{item.ClassificationConfidence!.Value.ToString("F2", CultureInfo.InvariantCulture)} (manual review recommended below {ManualReviewConfidenceThreshold.ToString(CultureInfo.InvariantCulture)})",
            ["requirement_validation"] = state.RequirementValidation ?? "not requested",
            ["manual_review"] = state.ManualReview ?? "not requested",
            ["stock"] = best is null
                ? "no origin has stock"
                : $"available at {best.OriginId} (effective lead time {best.EffectiveLeadTimeHours.ToString("0.#", CultureInfo.InvariantCulture)}h)"
        };

        if (state.ExcludedOrigins.Count > 0)
            facts["failed_origins"] = string.Join(", ", state.ExcludedOrigins);
        if (state.LastRejection is not null)
            facts["last_attempt"] = state.LastRejection;

        return facts;
    }

    // Returns true when the item reached a waiting/terminal state (stop asking).
    private bool Execute(OrderItem item, ItemDecisionState state, string tool)
    {
        state.LastRejection = null;

        switch (tool)
        {
            case "mark_item_digital_only":
                _backend.MarkItemDigitalOnly(item.ItemId);
                _backend.CompleteItem(item);
                return true;

            case "request_blocking_requirement_validation":
                var validated = _backend.RequestBlockingRequirementValidation(
                    item.ItemId, item.BlockingRequirementType!, $"attachment-{item.ItemId}");
                state.RequirementValidation = validated ? "approved" : "denied";
                return false;

            case "escalate_for_manual_review":
                var approved = _backend.EscalateForManualReview(
                    item.ItemId, $"classification confidence {item.ClassificationConfidence:F2}");
                state.ManualReview = approved ? "approved" : "rejected";
                return false;

            case "reserve_item_stock":
                var chosen = BestOrigin(item, state);
                if (chosen is null)
                {
                    state.LastRejection = "reserve_item_stock rejected: no origin has stock";
                    _backend.LogRejected(item.ItemId, tool, "no origin has stock");
                    return false;
                }

                item.ChosenOriginId = chosen.OriginId;
                _backend.SetShippingMethod(item.ItemId, item.ShippingMethod!);
                _backend.ReserveItemStock(item.ItemId, chosen.OriginId, item.QuantityRequested);

                // The fake has no real cancellation-window timer or handling delay - it walks
                // straight through these stages. A real backend would pause at each one.
                item.Status = ItemStatus.AwaitingCancellationWindow;
                item.Status = ItemStatus.ReadyForHandling;
                _backend.StartFulfillment(item.ItemId);
                item.Status = ItemStatus.Handling;
                item.Status = ItemStatus.VerifyingInvoice;
                return true;

            case "defer_item":
                _backend.DeferItem(item, state.Trigger);
                return true;

            case "cancel_item":
                var reason = state.RequirementValidation == "denied" ? $"{item.BlockingRequirementType} not validated"
                    : state.ManualReview == "rejected" ? "manual review rejected"
                    : state.Trigger;
                _backend.CancelItem(item, reason);
                return true;

            default:
                state.LastRejection = $"unknown tool {tool}";
                _backend.LogRejected(item.ItemId, tool, "unknown tool");
                return false;
        }
    }

    // Sourcing stays numeric: lowest effective lead time with stock, skipping failed origins.
    private SourcingCandidate? BestOrigin(OrderItem item, ItemDecisionState state) =>
        SourcingResolver.Resolve(_inventory.GetCandidates(item.ItemId)
            .Where(c => !state.ExcludedOrigins.Contains(c.OriginId))
            .ToList());

    private ItemDecisionState State(OrderItem item)
    {
        if (!_states.TryGetValue(item.ItemId, out var state))
            _states[item.ItemId] = state = new ItemDecisionState();
        return state;
    }

    private static void EnsureClassified(OrderItem item)
    {
        if (item.IsDigitalOnly is null || item.BlockingRequirementType is null ||
            item.ShippingMethod is null || item.ClassificationConfidence is null)
        {
            throw new InvalidOperationException($"Item {item.ItemId} has not been classified yet.");
        }
    }

    private sealed class ItemDecisionState
    {
        public string Trigger { get; set; } = "";
        public string? RequirementValidation { get; set; }
        public string? ManualReview { get; set; }
        public string? LastRejection { get; set; }
        public HashSet<string> ExcludedOrigins { get; } = new();
    }
}
