namespace JevWf.Evaluation.Scenarios;

// Outcome of running one scenario: empty Mismatches means the pipeline did exactly what was
// expected. ActualItemOutcomes/ActualShipments/ExecutionLog are kept (not just the diff) so the
// report can show what really happened even when a scenario passes. ExecutionLog is the actual
// ordered sequence of backend/tool calls Start + every HandleEventAsync/Finalize call made - it is
// not asserted on (too brittle to reorder-proof changes), only reported, to answer "what flow
// ran to fulfill this" alongside the aggregated outcome that IS asserted.
public sealed record ScenarioResult(
    string ScenarioName,
    IReadOnlyList<string> Mismatches,
    IReadOnlyDictionary<string, ExpectedItemOutcome> ActualItemOutcomes,
    IReadOnlyList<ExpectedShipment> ActualShipments,
    IReadOnlyList<string> ExecutionLog)
{
    public bool Passed => Mismatches.Count == 0;
}
