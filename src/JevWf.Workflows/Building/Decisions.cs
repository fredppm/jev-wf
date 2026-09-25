using JevWf.Workflows.Catalog;
using JevWf.Workflows.Orders;

namespace JevWf.Workflows.Building;

// Which pieces are active for each instance and, where several compete for the same type,
// which one goes. Without choices (default workflow), the piece flagged "default" goes.
internal sealed class Decisions
{
    public Dictionary<string, HashSet<string>> Active { get; } = new(StringComparer.Ordinal);

    public Dictionary<(string Instance, string Type), string> Choices { get; } = [];

    // The complete workflow: every condition the Jev would judge counts as true; rules still apply.
    public static Decisions Default(Order order, IReadOnlyList<PieceDefinition> pieces, IReadOnlyList<Instance> instances)
    {
        var decisions = new Decisions();
        foreach (var instance in instances)
        {
            var fields = RuleEvaluator.Fields(order, instance.Item);
            decisions.Active[instance.Key] = pieces
                .Where(p => p.Scope == instance.Scope)
                .Where(p => p.When?.Rule is not { } rule || RuleEvaluator.Evaluate(rule, fields))
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);
        }
        return decisions;
    }
}
