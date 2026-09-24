using System.Text.Json.Nodes;
using JevWf.Decisions;
using JevWf.Orders.Models;

namespace JevWf.Orders.Workflow;

// Code owns the workflow (not an agent): it asks the Jev a batch of atomic questions
// about the whole order in a single call, then deterministically decides, based on the
// answers + confidence, which OrderToolCatalog actions to trigger for each item.
public sealed class OrderWorkflowRunner
{
    private const double DecisionThreshold = 0.5;

    private readonly DecisionsClient _decisions;
    private readonly FakeOrderBackend _backend;

    public OrderWorkflowRunner(DecisionsClient decisions, FakeOrderBackend backend)
    {
        _decisions = decisions;
        _backend = backend;
    }

    public async Task RunAsync(Order order, CancellationToken ct = default)
    {
        var state = BuildState(order);
        var questions = BuildQuestions(order);

        var answers = await _decisions.AskAsync(state, questions, ct);

        var shippedNow = new List<string>();
        var deferred = new List<string>();

        foreach (var item in order.Items)
        {
            var isDigital = answers.Noul($"{item.ItemId}__is_digital_only") >= DecisionThreshold;
            if (isDigital)
            {
                _backend.MarkItemDigitalOnly(item.ItemId);
                _backend.CompleteItem(item);
                continue;
            }

            var requiresPrescription = answers.Noul($"{item.ItemId}__requires_prescription") >= DecisionThreshold;
            if (requiresPrescription)
            {
                var approved = _backend.RequestPrescriptionValidation(item.ItemId, $"attachment-{item.ItemId}");
                if (!approved)
                {
                    _backend.CancelItem(item, "prescription not validated");
                    continue;
                }
            }

            var shippingMethod = answers.Choice($"{item.ItemId}__shipping_urgency");
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

    private static JsonObject BuildState(Order order)
    {
        var itemsObj = new JsonObject();
        foreach (var item in order.Items)
        {
            itemsObj[item.ItemId] = new JsonObject
            {
                ["product_name"] = item.ProductName,
                ["description"] = item.Description,
                ["quantity_requested"] = item.QuantityRequested
            };
        }

        return new JsonObject
        {
            ["order_id"] = order.OrderId,
            ["items"] = itemsObj
        };
    }

    private static Dictionary<string, JsonObject> BuildQuestions(Order order)
    {
        var questions = new Dictionary<string, JsonObject>();

        foreach (var item in order.Items)
        {
            var path = $"items.{item.ItemId}";

            questions[$"{item.ItemId}__requires_prescription"] = DecisionQuestion.Noul(
                instructions: $"Does the product described in `{path}` require a medical prescription to be delivered to the customer?",
                trueWhen: "It is a controlled medication or one that legally requires a prescription",
                falseWhen: "It is a product that does not require a prescription");

            questions[$"{item.ItemId}__is_digital_only"] = DecisionQuestion.Noul(
                instructions: $"Is the product described in `{path}` entirely digital/virtual, with no physical shipping needed?",
                trueWhen: "It is delivered electronically (e.g. e-book, gift card, subscription, license)",
                falseWhen: "It is a physical good that needs to be shipped to the customer");

            questions[$"{item.ItemId}__shipping_urgency"] = DecisionQuestion.Choice(
                instructions: $"What shipping care does the product described in `{path}` require?",
                criteria: new Dictionary<string, string>
                {
                    ["standard"] = "Ordinary product, no special deadline or shipping care",
                    ["express"] = "Product that needs to arrive quickly, but doesn't require refrigeration",
                    ["refrigerated_express"] = "Perishable product that requires cold chain and fast delivery"
                });
        }

        return questions;
    }
}
