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
    // Picks ONE of request.AllowedTools (options compete with each other).
    Task<ToolDecision> ChooseAsync(ToolDecisionRequest request, CancellationToken ct = default);

    // Evaluates each of request.AllowedTools' conditions independently (yes/no per tool, no
    // competition). Returns tool name -> probability the condition holds (0-1).
    Task<IReadOnlyDictionary<string, double>> EvaluateConditionsAsync(ToolDecisionRequest request, CancellationToken ct = default);
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
        var questions = new Dictionary<string, JsonObject>
        {
            [QuestionId] = DecisionQuestion.Choice(
                instructions: "Which of these conditions best matches the current facts in `item.facts` for this order item?",
                criteria: request.AllowedTools)
        };

        var answers = await _decisions.AskAsync(BuildState(request), questions, ct);
        return new ToolDecision(answers.Choice(QuestionId), answers.Confidence(QuestionId));
    }

    public async Task<IReadOnlyDictionary<string, double>> EvaluateConditionsAsync(ToolDecisionRequest request, CancellationToken ct = default)
    {
        // One Noul per tool, all in a single call.
        var questions = request.AllowedTools.ToDictionary(
            t => t.Key,
            t => DecisionQuestion.Noul(
                instructions: "Does this condition hold for the current facts in `item.facts`?",
                trueWhen: t.Value,
                falseWhen: "The condition does not hold for these facts."));

        var answers = await _decisions.AskAsync(BuildState(request), questions, ct);
        return request.AllowedTools.Keys.ToDictionary(name => name, name => answers.Noul(name));
    }

    private static JsonObject BuildState(ToolDecisionRequest request)
    {
        var facts = new JsonObject();
        foreach (var (key, value) in request.Facts)
            facts[key] = value;

        return new JsonObject
        {
            ["item"] = new JsonObject
            {
                ["item_id"] = request.ItemId,
                ["product_name"] = request.ProductName,
                ["description"] = request.Description,
                ["facts"] = facts
            }
        };
    }
}
