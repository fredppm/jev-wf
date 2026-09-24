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
        var answers = await ClassifyRawAsync(orderId, items, ct);

        var result = new Dictionary<string, ItemAttributes>();
        foreach (var item in items)
        {
            var digitalNoul = answers.Noul($"{item.ItemId}__is_digital_only");
            var isDigitalOnly = digitalNoul >= DecisionThreshold;
            // Noul answers carry no confidence field - the value's distance from 0.5 already
            // fully describes the two-outcome distribution (confirmed in TypeSafe's docs).
            var digitalConfidence = Math.Abs(digitalNoul - 0.5) * 2;

            var blockingRequirementType = answers.Choice($"{item.ItemId}__blocking_requirement");
            var blockingRequirementConfidence = answers.Confidence($"{item.ItemId}__blocking_requirement");

            var shippingMethod = answers.Choice($"{item.ItemId}__shipping_urgency");
            var shippingConfidence = answers.Confidence($"{item.ItemId}__shipping_urgency");

            // Weakest link: if any one of the three answers was shaky, treat the whole item as
            // shaky - OrderWorkflowRunner escalates to manual review below its threshold.
            var confidence = Math.Min(digitalConfidence, Math.Min(blockingRequirementConfidence, shippingConfidence));

            result[item.ItemId] = new ItemAttributes(blockingRequirementType, isDigitalOnly, shippingMethod, confidence);
        }

        return result;
    }

    // Exposes the raw Decisions API response (confidence, full probability distribution per
    // option) instead of just the resolved attributes. Used by evaluation tooling that needs
    // to inspect calibration/consistency across repeated calls, not just the final decision.
    public async Task<JsonObject> ClassifyRawAsync(
        string orderId,
        IReadOnlyList<(string ItemId, string ProductName, string Description)> items,
        CancellationToken ct = default)
    {
        var state = BuildState(orderId, items);
        var questions = BuildQuestions(items);

        return await _decisions.AskAsync(state, questions, ct);
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

            // Generic blocking requirement instead of a dedicated bool per case: a new
            // requirement type is a new Choice option here, not a new question/field downstream.
            questions[$"{item.ItemId}__blocking_requirement"] = DecisionQuestion.Choice(
                instructions: $"Does the product described in `{path}` have a legal/regulatory requirement that must be validated before it can be delivered to the customer?",
                criteria: new Dictionary<string, string>
                {
                    ["none"] = "No such requirement - an ordinary product",
                    ["prescription"] = "It is a controlled medication or one that legally requires a prescription",
                    ["age_restricted"] = "It legally requires age verification before sale (e.g. alcohol, tobacco)"
                });

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
