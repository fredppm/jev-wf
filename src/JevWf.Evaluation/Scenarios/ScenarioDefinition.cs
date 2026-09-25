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
    // Premises about the outside world: tool name -> item id -> the result that external tool
    // returns (e.g. request_blocking_requirement_validation -> deniedmed1 -> "denied").
    // Unlisted items get the tool's first declared result (the passing one).
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ExternalToolResults = null);
