using JevWf.Decisions;
using JevWf.Evaluation.Scenarios;

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set the OPENROUTER_API_KEY environment variable with your OpenRouter key before running.");
    return 1;
}

// With no args, run every scenario. Args (if any) filter by scenario name, e.g.:
// dotnet run --project src/JevWf.Evaluation -- simple-shirt multi-seller-complex
var scenarios = args.Length == 0
    ? ScenarioCatalog.All
    : ScenarioCatalog.All.Where(s => args.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();

if (scenarios.Count == 0)
{
    Console.Error.WriteLine($"No scenario matches: {string.Join(", ", args)}");
    Console.Error.WriteLine($"Available scenarios: {string.Join(", ", ScenarioCatalog.All.Select(s => s.Name))}");
    return 1;
}

using var decisionsClient = new DecisionsClient(apiKey);
var runner = new ScenarioRunner(decisionsClient);

var results = new List<ScenarioResult>();
foreach (var scenario in scenarios)
{
    WriteLineColored($"Running '{scenario.Name}' - {scenario.Description}", ConsoleColor.Cyan);
    var result = await runner.RunAsync(scenario);
    results.Add(result);

    var productNameByItemId = scenario.Order.Items.ToDictionary(i => i.ItemId, i => i.ProductName);

    // The actual flow that ran: Start + every HandleEvent/Finalize call for the whole order,
    // not one workflow per item - this log is the ordered trace of every tool/backend action
    // triggered, per item and per shipment, across the scenario's whole event sequence.
    Console.WriteLine("  Execution log:");
    foreach (var entry in result.ExecutionLog)
        Console.WriteLine($"    - {entry}");

    Console.WriteLine("  Items:");
    foreach (var (itemId, outcome) in result.ActualItemOutcomes)
    {
        var flags = new List<string>();
        if (outcome.BlockingRequirementType != "none") flags.Add(outcome.BlockingRequirementType);
        if (outcome.IsDigitalOnly) flags.Add("digital");
        var flagsText = flags.Count > 0 ? $"{string.Join(", ", flags)} " : "";

        Console.WriteLine(
            $"    - {itemId} ({productNameByItemId[itemId]}): {flagsText}shipping={outcome.ShippingMethod} " +
            $"status={outcome.FinalStatus} origin={outcome.ChosenOriginId ?? "-"}");
    }

    Console.WriteLine("  Shipments:");
    if (result.ActualShipments.Count == 0)
    {
        Console.WriteLine("    (none)");
    }
    else
    {
        foreach (var shipment in result.ActualShipments)
            Console.WriteLine($"    - origin={shipment.OriginId} method={shipment.ShippingMethod} items=[{string.Join(", ", shipment.ItemIds)}]");
    }

    if (result.Passed)
    {
        WriteLineColored($"  [PASS] {scenario.Name}", ConsoleColor.Green);
    }
    else
    {
        WriteLineColored($"  [FAIL] {scenario.Name}", ConsoleColor.Red);
        foreach (var mismatch in result.Mismatches)
            WriteLineColored($"    - {mismatch}", ConsoleColor.Red);
    }

    Console.WriteLine();
}

var passedCount = results.Count(r => r.Passed);
var summaryColor = passedCount == results.Count ? ConsoleColor.Green : ConsoleColor.Red;
WriteLineColored($"== Summary: {passedCount}/{results.Count} scenarios passed ==", summaryColor);

return results.All(r => r.Passed) ? 0 : 1;

static void WriteLineColored(string text, ConsoleColor color)
{
    var previous = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ForegroundColor = previous;
}
