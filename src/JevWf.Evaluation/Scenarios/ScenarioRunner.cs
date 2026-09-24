using JevWf.Classification;
using JevWf.Decisions;
using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;
using JevWf.Orders.Workflow;

namespace JevWf.Evaluation.Scenarios;

// Runs a scenario end-to-end: real Jev classification, then the event-driven workflow where the
// real Jev picks each item-level tool. Compares the final outcome against what's expected -
// a wrong tool choice by the Jev shows up as a wrong final status/origin/shipment.
public sealed class ScenarioRunner
{
    private readonly DecisionsClient _decisions;

    public ScenarioRunner(DecisionsClient decisions)
    {
        _decisions = decisions;
    }

    public async Task<ScenarioResult> RunAsync(ScenarioDefinition scenario, CancellationToken ct = default)
    {
        var classifier = new OrderItemClassifier(_decisions);
        var attributes = await classifier.ClassifyAsync(
            scenario.Order.OrderId,
            scenario.Order.Items.Select(i => (i.ItemId, i.ProductName, i.Description)).ToList(),
            ct);

        foreach (var item in scenario.Order.Items)
        {
            var resolved = attributes[item.ItemId];
            item.BlockingRequirementType = resolved.BlockingRequirementType;
            item.IsDigitalOnly = resolved.IsDigitalOnly;
            item.ShippingMethod = resolved.ShippingMethod;
            item.ClassificationConfidence = resolved.Confidence;
        }

        var inventory = new FakeInventoryCatalog(scenario.SourcingCandidatesByItemId);
        var backend = new FakeOrderBackend(scenario.DeniedBlockingRequirementItemIds, scenario.ManualReviewRejectedItemIds);
        var runner = new OrderWorkflowRunner(backend, inventory, new JevToolSelector(_decisions));

        runner.Start(scenario.Order);

        // The scenario's event list drives everything past Start, including when shipment
        // grouping happens (via an explicit FinalizeEvent) relative to other events - e.g. a
        // CarrierDeliveredEvent must come after the FinalizeEvent that ships the item.
        foreach (var evt in scenario.Events)
            await runner.HandleEventAsync(scenario.Order, evt, ct);

        var actualItemOutcomes = BuildActualItemOutcomes(scenario.Order);
        var actualShipments = BuildActualShipments(scenario.Order);

        var mismatches = new List<string>();
        CompareItems(scenario.ExpectedItemOutcomes, actualItemOutcomes, mismatches);
        CompareShipments(scenario.ExpectedShipments, actualShipments, mismatches);

        return new ScenarioResult(scenario.Name, mismatches, actualItemOutcomes, actualShipments, backend.Log);
    }

    private static Dictionary<string, ExpectedItemOutcome> BuildActualItemOutcomes(Order order) =>
        order.Items.ToDictionary(
            i => i.ItemId,
            i => new ExpectedItemOutcome(
                i.BlockingRequirementType ?? "none",
                i.IsDigitalOnly ?? false,
                i.ShippingMethod ?? "-",
                i.Status,
                i.ChosenOriginId));

    // Shipments are derived from the order's own post-run state (ChosenOriginId/ShippingMethod
    // per item) - the same grouping OrderWorkflowRunner.Finalize itself does - rather than by
    // parsing the backend's string log, which would be brittle and would test log formatting,
    // not behavior. An item only carries an origin once it has actually shipped or later.
    private static List<ExpectedShipment> BuildActualShipments(Order order) =>
        order.Items
            .Where(i => i.ChosenOriginId is not null && i.Status is not (ItemStatus.Reserved or ItemStatus.AwaitingCancellationWindow
                or ItemStatus.ReadyForHandling or ItemStatus.Handling or ItemStatus.HandlingException or ItemStatus.VerifyingInvoice))
            .GroupBy(i => (Origin: i.ChosenOriginId!, Method: i.ShippingMethod!))
            .Select(g => new ExpectedShipment(g.Key.Origin, g.Key.Method, g.Select(i => i.ItemId).ToList()))
            .ToList();

    private static void CompareItems(
        IReadOnlyDictionary<string, ExpectedItemOutcome> expectedItemOutcomes,
        IReadOnlyDictionary<string, ExpectedItemOutcome> actualItemOutcomes,
        List<string> mismatches)
    {
        foreach (var (itemId, expected) in expectedItemOutcomes)
        {
            if (!actualItemOutcomes.TryGetValue(itemId, out var actual))
            {
                mismatches.Add($"{itemId}: expected in order but not found");
                continue;
            }

            if (actual.BlockingRequirementType != expected.BlockingRequirementType)
                mismatches.Add($"{itemId}: expected BlockingRequirementType={expected.BlockingRequirementType}, got={actual.BlockingRequirementType}");

            if (actual.IsDigitalOnly != expected.IsDigitalOnly)
                mismatches.Add($"{itemId}: expected IsDigitalOnly={expected.IsDigitalOnly}, got={actual.IsDigitalOnly}");

            if (actual.ShippingMethod != expected.ShippingMethod)
                mismatches.Add($"{itemId}: expected ShippingMethod={expected.ShippingMethod}, got={actual.ShippingMethod}");

            if (actual.FinalStatus != expected.FinalStatus)
                mismatches.Add($"{itemId}: expected FinalStatus={expected.FinalStatus}, got={actual.FinalStatus}");

            if (actual.ChosenOriginId != expected.ChosenOriginId)
                mismatches.Add($"{itemId}: expected ChosenOriginId={expected.ChosenOriginId ?? "-"}, got={actual.ChosenOriginId ?? "-"}");
        }
    }

    private static void CompareShipments(
        IReadOnlyList<ExpectedShipment> expectedShipments,
        IReadOnlyList<ExpectedShipment> actualShipments,
        List<string> mismatches)
    {
        var actualByKey = actualShipments.ToDictionary(s => (s.OriginId, s.ShippingMethod), s => s.ItemIds.ToHashSet());
        var expectedByKey = expectedShipments.ToDictionary(s => (s.OriginId, s.ShippingMethod), s => s.ItemIds.ToHashSet());

        foreach (var (key, expectedItemIds) in expectedByKey)
        {
            if (!actualByKey.TryGetValue(key, out var actualItemIds))
            {
                mismatches.Add($"shipment missing: origin={key.OriginId} method={key.ShippingMethod} expected items=[{string.Join(", ", expectedItemIds)}]");
                continue;
            }

            if (!actualItemIds.SetEquals(expectedItemIds))
            {
                mismatches.Add(
                    $"shipment origin={key.OriginId} method={key.ShippingMethod}: " +
                    $"expected items=[{string.Join(", ", expectedItemIds)}], got=[{string.Join(", ", actualItemIds)}]");
            }
        }

        foreach (var key in actualByKey.Keys.Except(expectedByKey.Keys))
        {
            mismatches.Add($"unexpected shipment: origin={key.OriginId} method={key.ShippingMethod} items=[{string.Join(", ", actualByKey[key])}]");
        }
    }
}
