using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;
using JevWf.Orders.Workflow;

namespace JevWf.Evaluation.Scenarios;

// A full test case for the solution: an order (real items, real free-text descriptions, real
// sellers) plus the sourcing candidates each item could ship from, the sequence of events the
// scenario replays after Start (payment clearing, restock, carrier delivery, ...), plus what the
// pipeline is expected to decide and execute. Running this exercises Classification (real Jev
// call) + Sourcing + the event-driven Workflow exactly as production code would.
public sealed record ScenarioDefinition(
    string Name,
    string Description,
    Order Order,
    Dictionary<string, List<SourcingCandidate>> SourcingCandidatesByItemId,
    IReadOnlyList<OrderEvent> Events,
    IReadOnlyDictionary<string, ExpectedItemOutcome> ExpectedItemOutcomes,
    IReadOnlyList<ExpectedShipment> ExpectedShipments,
    IReadOnlySet<string>? DeniedBlockingRequirementItemIds = null,
    IReadOnlySet<string>? ManualReviewRejectedItemIds = null);
