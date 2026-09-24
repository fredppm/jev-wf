namespace JevWf.Orders.Sourcing;

// One place an item could ship from (a store, a regional DC, a made-to-order factory...).
// All fields are objective, numeric, operational data - never something inferred from text.
public sealed record SourcingCandidate(
    string OriginId,
    int AvailableStock,
    double LeadTimeNominalHours,
    int CurrentQueue,
    double CapacityPerHour)
{
    // Nominal lead time alone ignores whether the origin is currently overloaded.
    // A store 1h away with a 40-order backlog can be slower in practice than a DC 24h away.
    public double EffectiveLeadTimeHours => LeadTimeNominalHours + (CurrentQueue / CapacityPerHour);
}
