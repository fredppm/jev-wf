namespace JevWf.Orders.Sourcing;

// Picks the best origin for an item from a list of candidates. Purely deterministic and
// numeric - it never talks to the Jev. "Best" means: has stock, and the lowest effective
// (demand-adjusted) lead time among those that do. Returns null when no candidate has stock.
public static class SourcingResolver
{
    public static SourcingCandidate? Resolve(IReadOnlyList<SourcingCandidate> candidates)
    {
        return candidates
            .Where(c => c.AvailableStock > 0)
            .OrderBy(c => c.EffectiveLeadTimeHours)
            .FirstOrDefault();
    }
}
