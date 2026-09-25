using System.Text.Json.Nodes;
using JevWf.Workflows.Catalog;
using JevWf.Workflows.Jev;
using JevWf.Workflows.Orders;
using JevWf.Workflows.Output;

namespace JevWf.Workflows.Building;

// Order group in, one workflow per order out. Per order:
//   1. rules decide which conditional pieces apply (code);
//   2. the Jev judges the "jev" conditions and picks between pieces competing for the same type;
//   3. the pieces are connected by their types and the graph is validated.
// If any Jev answer is below the confidence threshold, or the graph is invalid, the order gets
// the default workflow instead.
// trace, when given, receives every Jev answer and the reason for each fallback.
public sealed class WorkflowBuilder(
    PieceCatalog catalog,
    IJev jev,
    double confidenceThreshold = WorkflowBuilder.DefaultConfidenceThreshold,
    Action<string>? trace = null)
{
    public const double DefaultConfidenceThreshold = 0.7;

    public async Task<OrderGroupWorkflows> BuildAsync(OrderGroup orderGroup, CancellationToken ct = default)
    {
        var workflows = await Task.WhenAll(orderGroup.Orders.Select(order => BuildAsync(order, ct))).ConfigureAwait(false);
        return new OrderGroupWorkflows(orderGroup.OrderGroupId, workflows);
    }

    public async Task<OrderWorkflow> BuildAsync(Order order, CancellationToken ct = default)
    {
        var pieces = catalog.For(order.SellerId);
        var instances = Instance.For(order);

        var decisions = await DecideAsync(order, pieces, instances, ct).ConfigureAwait(false);
        if (decisions is not null)
        {
            var assembly = new WorkflowAssembly(catalog.Types, pieces, order, instances, decisions, WorkflowMode.Jev);
            if (assembly.Errors.Count == 0)
                return assembly.ToWorkflow();
            trace?.Invoke($"{order.OrderId}: default workflow, the Jev's workflow is invalid: {string.Join("; ", assembly.Errors)}");
        }

        var fallback = new WorkflowAssembly(catalog.Types, pieces, order, instances, Decisions.Default(order, pieces, instances), WorkflowMode.Default);
        if (fallback.Errors.Count > 0)
            throw new InvalidOperationException(
                $"The default workflow for order '{order.OrderId}' is invalid: {string.Join("; ", fallback.Errors)}");
        return fallback.ToWorkflow();
    }

    // Null when the Jev was not confident about some answer.
    private async Task<Decisions?> DecideAsync(Order order, IReadOnlyList<PieceDefinition> pieces, IReadOnlyList<Instance> instances, CancellationToken ct)
    {
        var results = await Task.WhenAll(instances.Select(i => DecideAsync(order, pieces, i, ct))).ConfigureAwait(false);
        if (results.Any(r => r is null))
            return null;

        var decisions = new Decisions();
        foreach (var (instance, result) in instances.Zip(results))
        {
            decisions.Active[instance.Key] = result!.Value.Active;
            foreach (var (type, piece) in result.Value.Choices)
                decisions.Choices[(instance.Key, type)] = piece;
        }
        return decisions;
    }

    private async Task<(HashSet<string> Active, Dictionary<string, string> Choices)?> DecideAsync(
        Order order, IReadOnlyList<PieceDefinition> pieces, Instance instance, CancellationToken ct)
    {
        var scoped = pieces.Where(p => p.Scope == instance.Scope).ToList();
        var fields = RuleEvaluator.Fields(order, instance.Item);
        var state = JevState(order, instance.Item);

        // Pieces sharing the same statement share one question (named after the first of them),
        // so they can never get different answers.
        var active = new HashSet<string>(StringComparer.Ordinal);
        var piecesByStatement = new Dictionary<string, List<(string Name, bool WhenTrue)>>(StringComparer.Ordinal);
        foreach (var piece in scoped)
        {
            if (piece.When?.Rule is { } rule && !RuleEvaluator.Evaluate(rule, fields))
                continue;

            if (piece.When?.Statement is { } statement)
                (piecesByStatement.TryGetValue(statement, out var list) ? list : piecesByStatement[statement] = [])
                    .Add((piece.Name, piece.When.Jev is not null));
            else
                active.Add(piece.Name);
        }

        if (piecesByStatement.Count > 0)
        {
            var conditions = piecesByStatement.ToDictionary(
                s => s.Value[0].Name,
                s => (JevQuestion)new YesNoQuestion($"Does this statement hold for the {Subject(instance)}?", s.Key, "The statement does not hold."),
                StringComparer.Ordinal);

            var answers = await jev.AskAsync(state, conditions, ct).ConfigureAwait(false);
            foreach (var sharing in piecesByStatement.Values)
            {
                var answer = answers[sharing[0].Name];
                Trace(order, instance, sharing[0].Name, answer.Yes is null ? "?" : answer.Yes.Value ? "yes" : "no", answer.Confidence);
                if (answer.Confidence < confidenceThreshold || answer.Yes is null)
                    return null;
                active.UnionWith(sharing.Where(p => p.WhenTrue == answer.Yes.Value).Select(p => p.Name));
            }
        }

        // Types where more than one active piece could move the flow forward.
        var graph = new ActivePieces(
            scoped.Where(p => active.Contains(p.Name)).ToList(),
            ActivePieces.EntryTypes(pieces, catalog.Types, instance.Scope));
        var choicePoints = graph.AcceptedTypes
            .Select(type => (Type: type, graph.SequenceFor(type).Advancing))
            .Where(point => point.Advancing.Count > 1)
            .ToDictionary(point => $"next_after_{point.Type}", StringComparer.Ordinal);

        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        if (choicePoints.Count > 0)
        {
            var questions = choicePoints.ToDictionary(
                point => point.Key,
                point => (JevQuestion)new ChoiceQuestion(
                    $"Which option is the right next step for the {Subject(instance)}?",
                    point.Value.Advancing.ToDictionary(p => p.Name, p => p.Description)),
                StringComparer.Ordinal);

            var answers = await jev.AskAsync(state, questions, ct).ConfigureAwait(false);
            foreach (var (id, point) in choicePoints)
            {
                var answer = answers[id];
                Trace(order, instance, id, answer.Choice ?? "?", answer.Confidence);
                if (answer.Confidence < confidenceThreshold || !point.Advancing.Any(p => p.Name == answer.Choice))
                    return null;
                choices[point.Type] = answer.Choice!;
            }
        }

        return (active, choices);
    }

    private void Trace(Order order, Instance instance, string question, string answer, double confidence)
    {
        if (trace is null)
            return;
        var below = confidence < confidenceThreshold ? $"  << below {confidenceThreshold:0.00}, default workflow" : "";
        trace($"{order.OrderId}/{instance.Key}: {question} = {answer} ({confidence:0.00}){below}");
    }

    private static string Subject(Instance instance) =>
        instance.Item is null ? "order in `order`" : "order item in `item`";

    private static JsonObject JevState(Order order, OrderItem? item)
    {
        var orderState = new JsonObject
        {
            ["orderId"] = order.OrderId,
            ["sellerId"] = order.SellerId,
            ["total"] = order.Total,
            ["itemCount"] = order.Items.Count
        };

        if (item is null)
        {
            orderState["items"] = new JsonArray([.. order.Items.Select(i => (JsonNode)ItemState(i))]);
            return new JsonObject { ["order"] = orderState };
        }

        return new JsonObject { ["order"] = orderState, ["item"] = ItemState(item) };
    }

    private static JsonObject ItemState(OrderItem item) => new()
    {
        ["itemId"] = item.ItemId,
        ["productName"] = item.ProductName,
        ["description"] = item.Description,
        ["quantity"] = item.Quantity,
        ["unitPrice"] = item.UnitPrice
    };
}
