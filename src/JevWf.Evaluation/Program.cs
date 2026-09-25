using System.Text.Json;
using System.Text.Json.Nodes;
using JevWf.Decisions;
using JevWf.Evaluation.Scenarios;
using JevWf.Orders.Workflow;

// --readme-only regenerates scenarios/<name>/README.md from the existing JSON files, no API call.
if (args.Contains("--readme-only"))
{
    foreach (var s in ScenarioCatalog.All)
    {
        var dir = Path.Combine("scenarios", s.Name);
        if (!File.Exists(Path.Combine(dir, "output.json"))) continue;
        var input = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "input.json")))!.AsObject();
        var output = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "output.json")))!.AsObject();
        File.WriteAllText(Path.Combine(dir, "README.md"), BuildScenarioReadme(s.Name, s.Description, input, output));
    }
    return 0;
}

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
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

var results = new List<ScenarioResult>();
foreach (var scenario in scenarios)
{
    WriteLineColored($"Running '{scenario.Name}' - {scenario.Description}", ConsoleColor.Cyan);

    // Captured before the run - Order.Items are still unmutated at this point.
    var inputJson = BuildInputJson(scenario);

    var result = await runner.RunAsync(scenario);
    results.Add(result);

    var outputJson = BuildOutputJson(result);
    var scenarioDir = Path.Combine("scenarios", scenario.Name);
    Directory.CreateDirectory(scenarioDir);
    File.WriteAllText(Path.Combine(scenarioDir, "input.json"), inputJson.ToJsonString(jsonOptions));
    File.WriteAllText(Path.Combine(scenarioDir, "output.json"), outputJson.ToJsonString(jsonOptions));
    File.WriteAllText(Path.Combine(scenarioDir, "README.md"), BuildScenarioReadme(scenario.Name, scenario.Description, inputJson, outputJson));

    var productNameByItemId = scenario.Order.Items.ToDictionary(i => i.ItemId, i => i.ProductName);

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

static JsonObject BuildInputJson(ScenarioDefinition scenario)
{
    var itemsArray = new JsonArray();
    foreach (var item in scenario.Order.Items)
    {
        itemsArray.Add(new JsonObject
        {
            ["itemId"] = item.ItemId,
            ["sellerId"] = item.SellerId,
            ["productName"] = item.ProductName,
            ["description"] = item.Description,
            ["quantityRequested"] = item.QuantityRequested,
            ["unitPrice"] = item.UnitPrice
        });
    }

    var candidatesObj = new JsonObject();
    foreach (var (itemId, candidates) in scenario.SourcingCandidatesByItemId)
    {
        var candidatesArray = new JsonArray();
        foreach (var c in candidates)
        {
            candidatesArray.Add(new JsonObject
            {
                ["originId"] = c.OriginId,
                ["availableStock"] = c.AvailableStock,
                ["leadTimeNominalHours"] = c.LeadTimeNominalHours,
                ["currentQueue"] = c.CurrentQueue,
                ["capacityPerHour"] = c.CapacityPerHour,
                ["effectiveLeadTimeHours"] = c.EffectiveLeadTimeHours
            });
        }
        candidatesObj[itemId] = candidatesArray;
    }

    var eventsArray = new JsonArray();
    foreach (var evt in scenario.Events)
        eventsArray.Add(EventToJson(evt));

    return new JsonObject
    {
        ["order"] = new JsonObject
        {
            ["orderId"] = scenario.Order.OrderId,
            ["customerEmail"] = scenario.Order.CustomerEmail,
            ["shippingAddress"] = scenario.Order.ShippingAddress,
            ["items"] = itemsArray
        },
        ["sourcingCandidates"] = candidatesObj,
        ["events"] = eventsArray
    };
}

static JsonObject EventToJson(OrderEvent evt) => evt switch
{
    PaymentApprovedEvent e => new JsonObject { ["type"] = "PaymentApprovedEvent", ["sellerId"] = e.SellerId },
    PaymentDeniedEvent e => new JsonObject { ["type"] = "PaymentDeniedEvent", ["sellerId"] = e.SellerId },
    RestockEvent e => new JsonObject
    {
        ["type"] = "RestockEvent",
        ["itemId"] = e.ItemId,
        ["originId"] = e.OriginId,
        ["newAvailableStock"] = e.NewAvailableStock
    },
    HandlingExceptionEvent e => new JsonObject { ["type"] = "HandlingExceptionEvent", ["itemId"] = e.ItemId, ["reason"] = e.Reason },
    CarrierDeliveredEvent e => new JsonObject { ["type"] = "CarrierDeliveredEvent", ["itemId"] = e.ItemId },
    ReturnRequestedEvent e => new JsonObject { ["type"] = "ReturnRequestedEvent", ["itemId"] = e.ItemId, ["reason"] = e.Reason },
    FinalizeEvent => new JsonObject { ["type"] = "FinalizeEvent" },
    _ => new JsonObject { ["type"] = evt.GetType().Name }
};

static string BuildScenarioReadme(string name, string description, JsonObject input, JsonObject output)
{
    static string S(JsonNode? n) => n?.ToString() ?? "-";

    var sb = new System.Text.StringBuilder();
    var passed = output["passed"]!.GetValue<bool>();
    sb.AppendLine($"# {name}\n");
    sb.AppendLine($"> {description}\n");
    sb.AppendLine($"**Result:** {(passed ? "✅ PASS" : "❌ FAIL")} · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)\n");

    sb.AppendLine("## Input\n");
    sb.AppendLine("What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.\n");
    sb.AppendLine("**Order items**\n");
    sb.AppendLine("| Item | Seller | Description (sent to the Jev) | Qty |");
    sb.AppendLine("|---|---|---|---|");
    foreach (var item in input["order"]!["items"]!.AsArray())
        sb.AppendLine($"| `{S(item!["itemId"])}` | {S(item["sellerId"])} | {S(item["description"])} | {S(item["quantityRequested"])} |");

    sb.AppendLine("\n**Sourcing candidates** (lowest effective lead time with stock wins)\n");
    sb.AppendLine("| Item | Origin | Stock | Effective lead time (h) |");
    sb.AppendLine("|---|---|---|---|");
    foreach (var (itemId, candidates) in input["sourcingCandidates"]!.AsObject())
        foreach (var c in candidates!.AsArray())
            sb.AppendLine($"| `{itemId}` | {S(c!["originId"])} | {S(c["availableStock"])} | {S(c["effectiveLeadTimeHours"])} |");

    sb.AppendLine("\n**Events** (in order)\n");
    var step = 1;
    foreach (var evt in input["events"]!.AsArray())
    {
        var fields = evt!.AsObject().Where(p => p.Key != "type").Select(p => $"{p.Key}={S(p.Value)}");
        sb.AppendLine($"{step++}. `{S(evt["type"])}` {string.Join(", ", fields)}".TrimEnd());
    }

    sb.AppendLine("\n## Output\n");
    sb.AppendLine("Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.\n");
    sb.AppendLine("| Item | Blocking req. | Digital | SLA | Origin | Final status |");
    sb.AppendLine("|---|---|---|---|---|---|");
    foreach (var (itemId, o) in output["items"]!.AsObject())
        sb.AppendLine($"| `{itemId}` | {S(o!["blockingRequirementType"])} | {(o["isDigitalOnly"]!.GetValue<bool>() ? "yes" : "-")} | {S(o["shippingMethod"])} | {S(o["chosenOriginId"])} | **{S(o["finalStatus"])}** |");

    sb.AppendLine("\n**Shipments** (grouped by origin + SLA)\n");
    var shipments = output["shipments"]!.AsArray();
    if (shipments.Count == 0)
    {
        sb.AppendLine("_none_");
    }
    else
    {
        sb.AppendLine("| Origin | SLA | Items |");
        sb.AppendLine("|---|---|---|");
        foreach (var sh in shipments)
            sb.AppendLine($"| {S(sh!["originId"])} | {S(sh["shippingMethod"])} | {string.Join(", ", sh["itemIds"]!.AsArray().Select(i => $"`{S(i)}`"))} |");
    }

    var mismatches = output["mismatches"]!.AsArray();
    if (mismatches.Count > 0)
    {
        sb.AppendLine("\n**Mismatches**\n");
        foreach (var m in mismatches)
            sb.AppendLine($"- {S(m)}");
    }

    sb.AppendLine("\n<details><summary>Execution log (tool calls)</summary>\n");
    sb.AppendLine("```");
    foreach (var line in output["executionLog"]!.AsArray())
        sb.AppendLine(S(line));
    sb.AppendLine("```\n</details>");

    return sb.ToString();
}

static JsonObject BuildOutputJson(ScenarioResult result)
{
    var itemsObj = new JsonObject();
    foreach (var (itemId, outcome) in result.ActualItemOutcomes)
    {
        itemsObj[itemId] = new JsonObject
        {
            ["blockingRequirementType"] = outcome.BlockingRequirementType,
            ["isDigitalOnly"] = outcome.IsDigitalOnly,
            ["shippingMethod"] = outcome.ShippingMethod,
            ["finalStatus"] = outcome.FinalStatus.ToString(),
            ["chosenOriginId"] = outcome.ChosenOriginId
        };
    }

    var shipmentsArray = new JsonArray();
    foreach (var s in result.ActualShipments)
    {
        shipmentsArray.Add(new JsonObject
        {
            ["originId"] = s.OriginId,
            ["shippingMethod"] = s.ShippingMethod,
            ["itemIds"] = new JsonArray(s.ItemIds.Select(id => (JsonNode)id).ToArray())
        });
    }

    return new JsonObject
    {
        ["scenario"] = result.ScenarioName,
        ["passed"] = result.Passed,
        ["mismatches"] = new JsonArray(result.Mismatches.Select(m => (JsonNode)m).ToArray()),
        ["executionLog"] = new JsonArray(result.ExecutionLog.Select(l => (JsonNode)l).ToArray()),
        ["items"] = itemsObj,
        ["shipments"] = shipmentsArray
    };
}
