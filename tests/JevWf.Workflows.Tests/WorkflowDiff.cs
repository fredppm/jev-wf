using JevWf.Workflows.Output;

namespace JevWf.Workflows.Tests;

// Readable differences between an expected and an actual set of workflows (order-insensitive).
internal static class WorkflowDiff
{
    public static IReadOnlyList<string> Between(OrderGroupWorkflows expected, OrderGroupWorkflows actual)
    {
        var differences = new List<string>();
        var expectedByOrder = expected.Workflows.ToDictionary(w => w.OrderId);
        var actualByOrder = actual.Workflows.ToDictionary(w => w.OrderId);

        foreach (var orderId in expectedByOrder.Keys.Except(actualByOrder.Keys))
            differences.Add($"missing workflow for order {orderId}");
        foreach (var orderId in actualByOrder.Keys.Except(expectedByOrder.Keys))
            differences.Add($"unexpected workflow for order {orderId}");

        foreach (var (orderId, want) in expectedByOrder)
        {
            if (!actualByOrder.TryGetValue(orderId, out var got))
                continue;

            if (want.Mode != got.Mode)
                differences.Add($"{orderId}: mode {got.Mode}, expected {want.Mode}");
            if (want.Start != got.Start)
                differences.Add($"{orderId}: start {got.Start}, expected {want.Start}");

            Compare(differences, orderId, "node", want.Nodes.Select(Describe), got.Nodes.Select(Describe));
            Compare(differences, orderId, "edge", want.Edges.Select(Describe), got.Edges.Select(Describe));
        }

        return differences;
    }

    private static void Compare(List<string> differences, string orderId, string what, IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var want = expected.ToHashSet(StringComparer.Ordinal);
        var got = actual.ToHashSet(StringComparer.Ordinal);
        differences.AddRange(want.Except(got).Select(x => $"{orderId}: missing {what} {x}"));
        differences.AddRange(got.Except(want).Select(x => $"{orderId}: unexpected {what} {x}"));
    }

    private static string Describe(WorkflowNode node) =>
        $"{node.Id} ({node.Piece}, {node.Scope}, {node.PublicStatus}, config={node.Config?.ToJsonString() ?? "-"})";

    private static string Describe(WorkflowEdge edge) =>
        $"{edge.From} -{edge.Port}[{edge.Type}]-> {edge.To ?? "end"}";
}
