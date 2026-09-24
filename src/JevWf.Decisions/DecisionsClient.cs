using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace JevWf.Decisions;

public sealed class DecisionsClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    public DecisionsClient(string apiKey, string model = "typesafe/jev-1.13")
    {
        _model = model;
        _http = new HttpClient { BaseAddress = new Uri("https://openrouter.ai/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // Returns the response's "answers" object: a map of question_id -> typed answer
    // ({"type", "noul"|"choice"|"score", "confidence", "probabilities"?}).
    public async Task<JsonObject> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JsonObject> questions,
        CancellationToken ct = default)
    {
        var questionsObj = new JsonObject();
        foreach (var (id, question) in questions)
            questionsObj[id] = question.DeepClone();

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["state"] = state.DeepClone(),
            ["questions"] = questionsObj
        };

        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("api/alpha/decisions", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Decisions API request failed ({(int)response.StatusCode}): {body}");

        var parsed = JsonNode.Parse(body)?.AsObject()
            ?? throw new InvalidOperationException("Empty response from the Decisions API.");

        return parsed["answers"]?.AsObject()
            ?? throw new InvalidOperationException($"Response missing 'answers' field: {body}");
    }

    public void Dispose() => _http.Dispose();
}

public static class DecisionAnswerExtensions
{
    public static double Noul(this JsonObject answers, string questionId) =>
        answers[questionId]!["noul"]!.GetValue<double>();

    public static string Choice(this JsonObject answers, string questionId) =>
        answers[questionId]!["choice"]!.GetValue<string>();

    public static double Confidence(this JsonObject answers, string questionId) =>
        answers[questionId]!["confidence"]!.GetValue<double>();
}
