using System.Text.Json;

namespace JevWf.Orders.Tools;

// A tool the Jev can choose at an item-level decision point, defined in tools/*.json - adding a
// tool is adding a file, no C# change (as long as it uses an existing Action).
//
// - WhenToUse: free text, written as a condition over the facts. It's the Choice option the Jev
//   matches against. Wording matters: conditions work, action descriptions don't.
// - Requires: hard guardrail checked by code (fact name -> allowed values). A tool that fails it
//   is never offered to the Jev.
// - Action: what the engine does when the tool is chosen. "external" runs the tool outside
//   (fake backend here) and stores its result in Produces.Fact; the others are built-in.
public sealed record DecisionTool(
    string Name,
    string Description,
    string WhenToUse,
    IReadOnlyList<string> OfferedAt,
    string Action,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Requires = null,
    ProducedFact? Produces = null);

public sealed record ProducedFact(string Fact, IReadOnlyList<string> Values);

public static class DecisionToolActions
{
    public const string CompleteDigital = "complete_digital";
    public const string ReserveStock = "reserve_stock";
    public const string Defer = "defer";
    public const string Cancel = "cancel";
    public const string External = "external";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { CompleteDigital, ReserveStock, Defer, Cancel, External };
}

public static class DecisionToolRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Loads every tools/*.json, searching up from the current directory so it works from the
    // repo root and from a project folder.
    public static IReadOnlyList<DecisionTool> Load(string? directory = null)
    {
        directory ??= FindToolsDirectory();
        var tools = Directory.GetFiles(directory, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonSerializer.Deserialize<DecisionTool>(File.ReadAllText(f), JsonOptions)
                         ?? throw new InvalidOperationException($"Empty tool definition: {f}"))
            .ToList();

        foreach (var tool in tools)
        {
            if (!DecisionToolActions.All.Contains(tool.Action))
                throw new InvalidOperationException($"Tool {tool.Name}: unknown action '{tool.Action}'.");
            if (tool.Action == DecisionToolActions.External && tool.Produces is null)
                throw new InvalidOperationException($"Tool {tool.Name}: external tools must declare 'produces'.");
        }

        return tools;
    }

    private static string FindToolsDirectory()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.json").Length > 0)
                return candidate;
        }

        throw new DirectoryNotFoundException("tools/ directory with *.json tool definitions not found.");
    }
}
