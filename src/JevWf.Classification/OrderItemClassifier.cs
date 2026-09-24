using System.Text.Json.Nodes;
using JevWf.Decisions;

namespace JevWf.Classification;

// The only place in the system that talks to the Jev: it asks a batch of atomic questions
// about every item's free-text name/description in a single call, and resolves each item's
// attributes. Everything downstream (JevWf.Orders) only works with the resolved attributes
// and has no knowledge that a model was involved.
public sealed class OrderItemClassifier
{
    private const double DecisionThreshold = 0.5;

    private readonly DecisionsClient _decisions;

    public OrderItemClassifier(DecisionsClient decisions)
    {
        _decisions = decisions;
    }

    public async Task<IReadOnlyDictionary<string, ItemAttributes>> ClassifyAsync(
        string orderId,
        IReadOnlyList<(string ItemId, string ProductName, string Description)> items,
        CancellationToken ct = default)
    {
        var state = BuildState(orderId, items);
        var questions = BuildQuestions(items);

        var answers = await _decisions.AskAsync(state, questions, ct);

        var result = new Dictionary<string, ItemAttributes>();
        foreach (var item in items)
        {
            var requiresPrescription = answers.Noul($"{item.ItemId}__requires_prescription") >= DecisionThreshold;
            var isDigitalOnly = answers.Noul($"{item.ItemId}__is_digital_only") >= DecisionThreshold;
            var shippingMethod = answers.Choice($"{item.ItemId}__shipping_urgency");

            result[item.ItemId] = new ItemAttributes(requiresPrescription, isDigitalOnly, shippingMethod);
        }

        return result;
    }

    private static JsonObject BuildState(
        string orderId,
        IReadOnlyList<(string ItemId, string ProductName, string Description)> items)
    {
        var itemsObj = new JsonObject();
        foreach (var item in items)
        {
            itemsObj[item.ItemId] = new JsonObject
            {
                ["product_name"] = item.ProductName,
                ["description"] = item.Description
            };
        }

        return new JsonObject
        {
            ["order_id"] = orderId,
            ["items"] = itemsObj
        };
    }

    private static Dictionary<string, JsonObject> BuildQuestions(
        IReadOnlyList<(string ItemId, string ProductName, string Description)> items)
    {
        var questions = new Dictionary<string, JsonObject>();

        foreach (var item in items)
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
