using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using JevWf.Classification;
using JevWf.Decisions;

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set the OPENROUTER_API_KEY environment variable with your OpenRouter key before running.");
    return 1;
}

var runCount = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 10;

// Same order/items every run - this measures consistency and latency, not adaptability.
var items = new List<(string ItemId, string ProductName, string Description)>
{
    ("item1", "Pistachio ice cream 1L", "Artisanal pistachio ice cream tub, must be kept frozen until delivery."),
    ("item2", "Amoxicillin 500mg", "Controlled antibiotic, requires a medical prescription to be presented."),
    ("item3", "E-book: Introduction to AI Workflows", "Digital PDF book, delivered by download/email after purchase."),
    ("item4", "Bluetooth headphones", "Wireless headphones, ordinary physical product, no delivery urgency."),
    ("item5", "Limited edition sneakers", "Collectible sneakers, ordinary physical product, no delivery urgency.")
};

var outputDir = Path.Combine("eval-runs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(outputDir);

using var decisionsClient = new DecisionsClient(apiKey);
var classifier = new OrderItemClassifier(decisionsClient);

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var runs = new List<(JsonObject Answers, double LatencyMs)>();

Console.WriteLine($"Running {runCount} identical classification calls against the real Decisions API...");

for (var i = 1; i <= runCount; i++)
{
    var stopwatch = Stopwatch.StartNew();
    var answers = await classifier.ClassifyRawAsync("eval-order", items);
    stopwatch.Stop();

    var latencyMs = stopwatch.Elapsed.TotalMilliseconds;
    runs.Add((answers, latencyMs));

    var runFile = Path.Combine(outputDir, $"run-{i:D3}.json");
    var runPayload = new JsonObject
    {
        ["run_index"] = i,
        ["timestamp_utc"] = DateTime.UtcNow.ToString("o"),
        ["latency_ms"] = latencyMs,
        ["answers"] = answers.DeepClone()
    };
    File.WriteAllText(runFile, runPayload.ToJsonString(jsonOptions));

    Console.WriteLine($"  run {i}/{runCount}: {latencyMs:F0} ms");
}

// ---- Consistency summary: for each question, did every run agree? ----
Console.WriteLine();
Console.WriteLine("== Consistency across runs ==");

// Union across all runs, not just run 1 - a question missing from some runs' answers is
// itself a reliability signal worth surfacing, not something to crash or silently ignore.
var questionIds = runs.SelectMany(r => r.Answers.Select(kv => kv.Key)).Distinct().OrderBy(k => k).ToList();
var summary = new JsonObject();

foreach (var questionId in questionIds)
{
    var answered = runs.Where(r => r.Answers[questionId] is not null).Select(r => r.Answers[questionId]!).ToList();
    var missingCount = runs.Count - answered.Count;

    if (answered.Count == 0)
    {
        Console.WriteLine($"- {questionId}: MISSING from all {runs.Count} runs");
        summary[questionId] = new JsonObject { ["missing_count"] = runs.Count };
        continue;
    }

    var type = answered[0]["type"]!.GetValue<string>();
    var missingNote = missingCount > 0 ? $" MISSING in {missingCount}/{runs.Count} runs" : "";

    string line;
    JsonObject questionSummary;

    if (type == "noul")
    {
        // A Noul has no separate confidence field - the doc is explicit that the noul value
        // alone fully describes the two-outcome distribution. Distance from 0.5 stands in for it.
        var values = answered.Select(a => a["noul"]!.GetValue<double>()).ToList();
        var confidences = values.Select(v => Math.Abs(v - 0.5) * 2).ToList();
        var sides = values.Select(v => v >= 0.5).Distinct().Count();
        var consistent = sides == 1 && missingCount == 0;

        line = $"{questionId} (noul): consistent={(consistent ? "YES" : "NO")}{missingNote} " +
               $"values=[{string.Join(", ", values.Select(v => v.ToString("F2")))}] " +
               $"implied confidence avg={confidences.Average():F2} (min {confidences.Min():F2}, max {confidences.Max():F2})";

        questionSummary = new JsonObject
        {
            ["type"] = type,
            ["consistent"] = consistent,
            ["missing_count"] = missingCount,
            ["values"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()),
            ["implied_confidence_avg"] = confidences.Average(),
            ["implied_confidence_min"] = confidences.Min(),
            ["implied_confidence_max"] = confidences.Max()
        };
    }
    else
    {
        var choices = answered.Select(a => a["choice"]!.GetValue<string>()).ToList();
        var confidences = answered.Select(a => a["confidence"]!.GetValue<double>()).ToList();
        var consistent = choices.Distinct().Count() == 1 && missingCount == 0;

        line = $"{questionId} (choice): consistent={(consistent ? "YES" : "NO")}{missingNote} " +
               $"choices=[{string.Join(", ", choices)}] " +
               $"confidence avg={confidences.Average():F2} (min {confidences.Min():F2}, max {confidences.Max():F2})";

        questionSummary = new JsonObject
        {
            ["type"] = type,
            ["consistent"] = consistent,
            ["missing_count"] = missingCount,
            ["choices"] = new JsonArray(choices.Select(v => (JsonNode)v).ToArray()),
            ["confidence_avg"] = confidences.Average(),
            ["confidence_min"] = confidences.Min(),
            ["confidence_max"] = confidences.Max()
        };
    }

    Console.WriteLine($"- {line}");
    summary[questionId] = questionSummary;
}

// ---- Latency summary ----
var latencies = runs.Select(r => r.LatencyMs).OrderBy(l => l).ToList();
double Percentile(double p)
{
    var index = (int)Math.Ceiling(p / 100.0 * latencies.Count) - 1;
    return latencies[Math.Clamp(index, 0, latencies.Count - 1)];
}

Console.WriteLine();
Console.WriteLine("== Latency (ms) ==");
Console.WriteLine($"min={latencies.Min():F0} avg={latencies.Average():F0} p50={Percentile(50):F0} p95={Percentile(95):F0} max={latencies.Max():F0}");

var latencySummary = new JsonObject
{
    ["min_ms"] = latencies.Min(),
    ["avg_ms"] = latencies.Average(),
    ["p50_ms"] = Percentile(50),
    ["p95_ms"] = Percentile(95),
    ["max_ms"] = latencies.Max()
};

var summaryPayload = new JsonObject
{
    ["run_count"] = runCount,
    ["questions"] = summary,
    ["latency"] = latencySummary
};
File.WriteAllText(Path.Combine(outputDir, "summary.json"), summaryPayload.ToJsonString(jsonOptions));

Console.WriteLine();
Console.WriteLine($"Raw runs and summary saved to {outputDir}");

return 0;
