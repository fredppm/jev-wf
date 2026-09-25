using System.Globalization;
using JevWf.Classification;
using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;
using JevWf.Orders.Tools;

namespace JevWf.Orders.Workflow;

// Event-driven order workflow with a generic decision engine: the tools the Jev can choose are
// data (tools/*.json, see DecisionTool), not code.
//
// At each item-level decision point (payment approved, restock, handling exception):
// 1. build the facts for the item (classification, stock, results of tools already run...)
// 2. offer the tools whose OfferedAt matches and whose Requires passes against the facts
// 3. the Jev picks one by matching the facts against each tool's WhenToUse
// 4. run the tool's Action; an external tool stores its result as a new fact -> back to 1,
//    until the item reaches a waiting/terminal state.
//
// Steps with no real alternative stay deterministic: seller confirmation/payment gate (Start),
// payment denial, shipment grouping (Finalize - pure math), carrier delivery and returns.
public sealed class OrderWorkflowRunner
{
    private const int MaxDecisionSteps = 6;

    // Shown to the Jev as a policy hint next to the classification confidence.
    private const double ManualReviewConfidenceThreshold = 0.75;

    private static readonly HashSet<ItemStatus> TerminalStatuses = new()
    {
        ItemStatus.Delivered, ItemStatus.Cancelled, ItemStatus.Returned, ItemStatus.Refunded
    };

    private readonly FakeOrderBackend _backend;
    private readonly FakeInventoryCatalog _inventory;
    private readonly IToolSelector _selector;
    private readonly IReadOnlyList<DecisionTool> _tools;
    private readonly Dictionary<string, ItemDecisionState> _states = new();

    public OrderWorkflowRunner(
        FakeOrderBackend backend,
        FakeInventoryCatalog inventory,
        IToolSelector selector,
        IReadOnlyList<DecisionTool> tools)
    {
        _backend = backend;
        _inventory = inventory;
        _selector = selector;
        _tools = tools;
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
                    State(item).SetTrigger(DecisionPoints.PaymentApproved, $"payment approved by {approved.SellerId}");
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
                State(deferredItem).SetTrigger(DecisionPoints.Restock, $"restock at {restock.OriginId} (new stock {restock.NewAvailableStock})");
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
                state.SetTrigger(DecisionPoints.HandlingException, $"handling exception: {handlingException.Reason}");
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
            var facts = BuildFacts(item, state);
            var offered = _tools.Where(t => t.OfferedAt.Contains(state.DecisionPoint) && RequiresPass(t, facts)).ToList();

            // Checks first: each external tool's condition is evaluated on its own (yes/no), so a
            // specific check never loses to a generic action that also matches. Every check that
            // applies runs; then the facts are rebuilt and we ask again.
            var checks = offered.Where(t => t.Action == DecisionToolActions.External).ToList();
            if (checks.Count > 0)
            {
                var applies = await _selector.EvaluateConditionsAsync(
                    new ToolDecisionRequest(item.ItemId, item.ProductName, item.Description, facts,
                        checks.ToDictionary(t => t.Name, t => t.WhenToUse)), ct);

                _backend.LogChecks(item.ItemId, applies);

                var toRun = checks.Where(t => applies[t.Name] >= 0.5).ToList();
                foreach (var check in toRun)
                    Execute(item, state, check, facts);
                if (toRun.Count > 0)
                    continue;
            }

            var actions = offered.Where(t => t.Action != DecisionToolActions.External).ToList();
            if (actions.Count == 0)
            {
                _backend.LogRejected(item.ItemId, "decision", $"no action offered at {state.DecisionPoint}");
                return;
            }
            offered = actions;

            var decision = await _selector.ChooseAsync(
                new ToolDecisionRequest(item.ItemId, item.ProductName, item.Description, facts,
                    offered.ToDictionary(t => t.Name, t => t.WhenToUse)), ct);

            _backend.LogDecision(item.ItemId, offered.Select(t => t.Name), decision.Tool, decision.Confidence);

            var tool = offered.SingleOrDefault(t => t.Name == decision.Tool);
            if (tool is null)
            {
                state.LastRejection = $"{decision.Tool} was not offered";
                _backend.LogRejected(item.ItemId, decision.Tool, "not offered");
                continue;
            }

            if (Execute(item, state, tool, facts))
                return;
        }

        _backend.LogRejected(item.ItemId, "decision_loop", $"no final action after {MaxDecisionSteps} decisions");
    }

    private static bool RequiresPass(DecisionTool tool, IReadOnlyDictionary<string, string> facts) =>
        tool.Requires is null ||
        tool.Requires.All(r => facts.TryGetValue(r.Key, out var value) && r.Value.Contains(value));

    // The fact vocabulary tools can reference in WhenToUse/Requires. Every fact an external tool
    // produces starts as 'not requested'; a result other than the tool's first declared value
    // (by convention the passing one, e.g. "approved") counts as a failed check.
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
                $"{item.ClassificationConfidence!.Value.ToString("F2", CultureInfo.InvariantCulture)} (manual review threshold {ManualReviewConfidenceThreshold.ToString(CultureInfo.InvariantCulture)})",
            ["item_value"] = item.UnitPrice is decimal price
                ? (price * item.QuantityRequested).ToString("0.00", CultureInfo.InvariantCulture)
                : "unknown",
            ["stock"] = best is null
                ? "no origin has stock"
                : $"available at {best.OriginId} (effective lead time {best.EffectiveLeadTimeHours.ToString("0.#", CultureInfo.InvariantCulture)}h)"
        };

        var failed = new List<string>();
        foreach (var produced in _tools.Where(t => t.Produces is not null).Select(t => t.Produces!))
        {
            if (state.Produced.TryGetValue(produced.Fact, out var value))
            {
                facts[produced.Fact] = value;
                if (value != produced.Values[0])
                    failed.Add($"{produced.Fact}={value}");
            }
            else
            {
                // The one built-in exception: no legal requirement means nothing to validate.
                facts[produced.Fact] = produced.Fact == "requirement_validation" && item.BlockingRequirementType == "none"
                    ? "not required"
                    : "not requested";
            }
        }

        facts["failed_checks"] = failed.Count == 0 ? "none" : string.Join(", ", failed);

        if (state.ExcludedOrigins.Count > 0)
            facts["failed_origins"] = string.Join(", ", state.ExcludedOrigins);
        if (state.LastRejection is not null)
            facts["last_attempt"] = state.LastRejection;

        return facts;
    }

    // Returns true when the item reached a waiting/terminal state (stop asking).
    private bool Execute(OrderItem item, ItemDecisionState state, DecisionTool tool, IReadOnlyDictionary<string, string> facts)
    {
        state.LastRejection = null;

        switch (tool.Action)
        {
            case DecisionToolActions.External:
                state.Produced[tool.Produces!.Fact] = _backend.RunExternalTool(tool.Name, item.ItemId, tool.Produces.Values);
                return false;

            case DecisionToolActions.CompleteDigital:
                _backend.MarkItemDigitalOnly(item.ItemId);
                _backend.CompleteItem(item);
                return true;

            case DecisionToolActions.ReserveStock:
                var chosen = BestOrigin(item, state);
                if (chosen is null)
                {
                    state.LastRejection = $"{tool.Name} rejected: no origin has stock";
                    _backend.LogRejected(item.ItemId, tool.Name, "no origin has stock");
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

            case DecisionToolActions.Defer:
                _backend.DeferItem(item, state.Trigger);
                return true;

            case DecisionToolActions.Cancel:
                _backend.CancelItem(item, facts["failed_checks"] != "none" ? $"failed checks: {facts["failed_checks"]}" : state.Trigger);
                return true;

            default:
                throw new InvalidOperationException($"Unknown action {tool.Action}");
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
        public string DecisionPoint { get; private set; } = "";
        public string Trigger { get; private set; } = "";
        public string? LastRejection { get; set; }
        public Dictionary<string, string> Produced { get; } = new();
        public HashSet<string> ExcludedOrigins { get; } = new();

        public void SetTrigger(string decisionPoint, string trigger)
        {
            DecisionPoint = decisionPoint;
            Trigger = trigger;
        }
    }
}

// Names used in DecisionTool.OfferedAt.
public static class DecisionPoints
{
    public const string PaymentApproved = "payment_approved";
    public const string Restock = "restock";
    public const string HandlingException = "handling_exception";
}
