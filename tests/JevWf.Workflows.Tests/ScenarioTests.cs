using FluentAssertions;
using JevWf.Workflows.Building;
using JevWf.Workflows.Catalog;
using JevWf.Workflows.Jev;
using JevWf.Workflows.Orders;
using JevWf.Workflows.Output;

namespace JevWf.Workflows.Tests;

// Each scenario is an order group (input.json) and the workflows it must produce (expected.json).
// jev-answers.json holds the decisions the Jev is expected to make, so the build can be checked
// without calling the model; the live test checks that the real Jev gets to the same workflows.
public sealed class ScenarioTests
{
    public static TheoryData<string> Scenarios { get; } =
        new(Directory.GetDirectories(TestPaths.Scenarios).Select(Path.GetFileName).Order()!);

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Builds_the_expected_workflows_from_the_expected_jev_decisions(string scenario)
    {
        var jev = ScriptedJev.Load(ScenarioFile(scenario, "jev-answers.json"));

        var actual = await Build(scenario, jev);

        WorkflowDiff.Between(Expected(scenario), actual).Should().BeEmpty();
    }

    [SkippableTheory]
    [MemberData(nameof(Scenarios))]
    public async Task Builds_the_expected_workflows_with_the_real_jev(string scenario)
    {
        var apiKey = JevApiKey.Find(TestPaths.Root);
        Skip.If(apiKey is null, "OPENROUTER_API_KEY is not set (environment or .env).");
        using var jev = new OpenRouterJev(apiKey!);

        var actual = await Build(scenario, jev);

        WorkflowDiff.Between(Expected(scenario), actual).Should().BeEmpty();
    }

    private static Task<OrderGroupWorkflows> Build(string scenario, IJev jev) =>
        new WorkflowBuilder(PieceCatalog.Load(TestPaths.Catalog), jev)
            .BuildAsync(JsonDefaults.Read<OrderGroup>(ScenarioFile(scenario, "input.json")));

    private static OrderGroupWorkflows Expected(string scenario) =>
        JsonDefaults.Read<OrderGroupWorkflows>(ScenarioFile(scenario, "expected.json"));

    private static string ScenarioFile(string scenario, string file) =>
        Path.Combine(TestPaths.Scenarios, scenario, file);
}
