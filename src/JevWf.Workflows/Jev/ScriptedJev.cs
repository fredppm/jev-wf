using System.Text.Json.Nodes;

namespace JevWf.Workflows.Jev;

// Answers from a fixed script instead of the model, for tests and for writing expected workflows.
// Script keys are "<orderId>" (order questions) or "<orderId>/<itemId>" (item questions).
// Unscripted yes/no questions answer "no"; unscripted choices pick the first option.
public sealed class ScriptedJev(IReadOnlyDictionary<string, Dictionary<string, JevAnswer>> script) : IJev
{
    public static ScriptedJev Load(string path) =>
        new(JsonDefaults.Read<Dictionary<string, Dictionary<string, JevAnswer>>>(path));

    public Task<IReadOnlyDictionary<string, JevAnswer>> AskAsync(
        JsonObject state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken ct = default)
    {
        var key = state["order"]!["orderId"]!.GetValue<string>();
        if (state["item"]?["itemId"]?.GetValue<string>() is { } itemId)
            key += "/" + itemId;

        var scripted = script.GetValueOrDefault(key);
        IReadOnlyDictionary<string, JevAnswer> answers = questions.ToDictionary(
            q => q.Key,
            q => scripted?.GetValueOrDefault(q.Key) ?? Unscripted(q.Value));
        return Task.FromResult(answers);
    }

    private static JevAnswer Unscripted(JevQuestion question) => question switch
    {
        ChoiceQuestion choice => new JevAnswer(Choice: choice.Options.Keys.First()),
        _ => new JevAnswer(Yes: false)
    };
}
