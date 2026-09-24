namespace JevWf.Orders.Sourcing;

// In-memory stand-in for a real multi-location inventory/logistics system. Returns the
// sourcing candidates for an item: where it could ship from, and each location's current
// stock, nominal lead time, and load (queue/capacity).
public sealed class FakeInventoryCatalog
{
    private readonly Dictionary<string, List<SourcingCandidate>> _candidatesByItemId;

    public FakeInventoryCatalog(Dictionary<string, List<SourcingCandidate>> candidatesByItemId)
    {
        _candidatesByItemId = candidatesByItemId;
    }

    public IReadOnlyList<SourcingCandidate> GetCandidates(string itemId) =>
        _candidatesByItemId.GetValueOrDefault(itemId, new List<SourcingCandidate>());

    // Simulates a restock: replaces the stock level of one item's candidate at one origin.
    // Used to react to RestockEvent and make a previously-deferred item sourceable again.
    public void Restock(string itemId, string originId, int newAvailableStock)
    {
        var candidates = _candidatesByItemId.GetValueOrDefault(itemId);
        if (candidates is null)
            throw new InvalidOperationException($"No known sourcing candidates for item {itemId}.");

        var index = candidates.FindIndex(c => c.OriginId == originId);
        if (index < 0)
            throw new InvalidOperationException($"Item {itemId} has no candidate at origin {originId}.");

        candidates[index] = candidates[index] with { AvailableStock = newAvailableStock };
    }
}
