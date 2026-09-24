using JevWf.Orders.Models;

namespace JevWf.Evaluation.Scenarios;

// What a scenario asserts about one item after the full pipeline (Start -> events -> Finalize)
// runs. Attributes are checked separately from status/origin so a failure report can say
// *where* it broke: the Jev misclassified it, or classification was right but sourcing/execution
// acted on it wrong. Confidence is intentionally not asserted here - it's a real, continuous
// value from the live Jev call, not something a scenario can pin to an exact number.
public sealed record ExpectedItemOutcome(
    string BlockingRequirementType,
    bool IsDigitalOnly,
    string ShippingMethod,
    ItemStatus FinalStatus,
    string? ChosenOriginId);

// One expected shipment: items sharing an origin and shipping method. Item order doesn't
// matter - comparison treats ItemIds as a set.
public sealed record ExpectedShipment(
    string OriginId,
    string ShippingMethod,
    IReadOnlyList<string> ItemIds);
