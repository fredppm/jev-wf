using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace JevWf.Workflows.Jev;

// The Jev through OpenRouter's Decisions API (POST https://openrouter.ai/api/alpha/decisions).
public sealed class OpenRouterJev : IJev, IDisposable
{
    public const string DefaultModel = "typesafe/jev-1.13";

    private readonly HttpClient _http;
    private readonly string _model;

    public OpenRouterJev(string apiKey, string model = DefaultModel)
    {
        _model = model;
        _http = new HttpClient { BaseAddress = new Uri("https://openrouter.ai/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<IReadOnlyDictionary<string, JevAnswer>> AskAsync(
        JsonObject state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["state"] = state.DeepClone(),
            ["questions"] = new JsonObject(questions.Select(q => KeyValuePair.Create(q.Key, (JsonNode?)ToJson(q.Value))))
        };

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("api/alpha/decisions", content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Jev request failed ({(int)response.StatusCode}): {text}");

        var answers = JsonNode.Parse(text)?["answers"]?.AsObject()
            ?? throw new InvalidDataException($"Jev response has no 'answers': {text}");

        return questions.ToDictionary(q => q.Key, q => Parse(q.Value, answers[q.Key]
            ?? throw new InvalidDataException($"Jev did not answer '{q.Key}': {text}")));
    }

    public void Dispose() => _http.Dispose();

    private static JsonObject ToJson(JevQuestion question) => question switch
    {
        YesNoQuestion yesNo => new JsonObject
        {
            ["type"] = "noul",
            ["instructions"] = yesNo.Instructions,
            ["criteria"] = new JsonObject { ["true"] = yesNo.TrueWhen, ["false"] = yesNo.FalseWhen }
        },
        ChoiceQuestion choice => new JsonObject
        {
            ["type"] = "choice",
            ["instructions"] = choice.Instructions,
            ["criteria"] = new JsonObject(choice.Options.Select(o => KeyValuePair.Create(o.Key, (JsonNode?)o.Value)))
        },
        _ => throw new ArgumentOutOfRangeException(nameof(question))
    };

    // "noul" is the probability (0-1) that the statement holds and comes without a confidence:
    // the confidence is how far the probability is from a coin flip (0.95 and 0.05 are both 0.95).
    private static JevAnswer Parse(JevQuestion question, JsonNode answer)
    {
        if (question is YesNoQuestion)
        {
            var probability = Number(answer, "noul");
            return new JevAnswer(Yes: probability >= 0.5, Confidence: Math.Max(probability, 1 - probability));
        }

        var choice = answer["choice"]?.GetValue<string>()
            ?? throw new InvalidDataException($"Jev choice answer has no 'choice': {answer.ToJsonString()}");
        return new JevAnswer(Choice: choice, Confidence: Number(answer, "confidence"));
    }

    private static double Number(JsonNode answer, string field) =>
        answer[field]?.GetValue<double>()
        ?? throw new InvalidDataException($"Jev answer has no '{field}': {answer.ToJsonString()}");
}
