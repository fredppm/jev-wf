using System.Globalization;
using JevWf.Workflows;
using JevWf.Workflows.Building;
using JevWf.Workflows.Catalog;
using JevWf.Workflows.Jev;
using JevWf.Workflows.Orders;

// jevwf <orderGroup.json> [--catalog <dir>] [--threshold <0-1>] [--answers <jev-answers.json>] [--out <file>]
if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: jevwf <orderGroup.json> [--catalog <dir>] [--threshold <0-1>] [--answers <jev-answers.json>] [--out <file>]");
    return 2;
}

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i + 1 < args.Length; i += 2)
    options[args[i]] = args[i + 1];

var catalog = PieceCatalog.Load(options.GetValueOrDefault("--catalog", "catalog"));
var threshold = options.TryGetValue("--threshold", out var t)
    ? double.Parse(t, CultureInfo.InvariantCulture)
    : WorkflowBuilder.DefaultConfidenceThreshold;

IJev jev;
if (options.TryGetValue("--answers", out var answersPath))
{
    jev = ScriptedJev.Load(answersPath);
}
else
{
    var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
    {
        Console.Error.WriteLine("Set OPENROUTER_API_KEY, or pass --answers to use scripted Jev answers.");
        return 2;
    }
    jev = new OpenRouterJev(apiKey);
}

var orderGroup = JsonDefaults.Read<OrderGroup>(args[0]);
var workflows = await new WorkflowBuilder(catalog, jev, threshold).BuildAsync(orderGroup);
(jev as IDisposable)?.Dispose();

var json = JsonDefaults.Write(workflows);
if (options.TryGetValue("--out", out var outPath))
    File.WriteAllText(outPath, json + Environment.NewLine);
else
    Console.WriteLine(json);
return 0;
