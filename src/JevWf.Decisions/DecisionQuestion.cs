using System.Text.Json.Nodes;

namespace JevWf.Decisions;

// Builds questions in the format accepted by the "questions" field of the Decisions API
// (POST https://openrouter.ai/api/alpha/decisions, model typesafe/jev-1.13).
public static class DecisionQuestion
{
    public static JsonObject Noul(string instructions, string trueWhen, string falseWhen) => new()
    {
        ["type"] = "noul",
        ["instructions"] = instructions,
        ["criteria"] = new JsonObject
        {
            ["true"] = trueWhen,
            ["false"] = falseWhen
        }
    };

    public static JsonObject Choice(string instructions, IReadOnlyDictionary<string, string> criteria)
    {
        var criteriaObj = new JsonObject();
        foreach (var (option, description) in criteria)
            criteriaObj[option] = description;

        return new JsonObject
        {
            ["type"] = "choice",
            ["instructions"] = instructions,
            ["criteria"] = criteriaObj
        };
    }
}
