using System.Text.Json.Nodes;
using JevWf.Decisions;

namespace JevWf.Classification;

// What the workflow knows about one item at a decision point, plus the tools it is allowed to
// run from there (name -> description). The workflow decides which tools are legal; the Jev
// picks one of them.
public sealed record ToolDecisionRequest(
    string ItemId,
    string ProductName,
    string Description,
    IReadOnlyDictionary<string, string> Facts,
    IReadOnlyDictionary<string, string> AllowedTools);

public sealed record ToolDecision(string Tool, double Confidence);

public interface IToolSelector
{
    Task<ToolDecision> ChooseAsync(ToolDecisionRequest request, CancellationToken ct = default);
}

// Asks the Jev which tool to run next for an item, as a single Choice question whose options
// are exactly the allowed tools.
public sealed class JevToolSelector : IToolSelector
{
    private const string QuestionId = "next_tool";

    private readonly DecisionsClient _decisions;

    public JevToolSelector(DecisionsClient decisions)
    {
        _decisions = decisions;
    }

    public async Task<ToolDecision> ChooseAsync(ToolDecisionRequest request, CancellationToken ct = default)
    {
        var facts = new JsonObject();
        foreach (var (key, value) in request.Facts)
            facts[key] = value;

        var state = new JsonObject
        {
            ["item"] = new JsonObject
            {
                ["item_id"] = request.ItemId,
                ["product_name"] = request.ProductName,
                ["description"] = request.Description,
                ["facts"] = facts
            }
        };

        var questions = new Dictionary<string, JsonObject>
        {
            [QuestionId] = DecisionQuestion.Choice(
                instructions: "Which of these conditions best matches the current facts in `item.facts` for this order item?",
                criteria: request.AllowedTools)
        };

        var answers = await _decisions.AskAsync(state, questions, ct);
        return new ToolDecision(answers.Choice(QuestionId), answers.Confidence(QuestionId));
    }
}
