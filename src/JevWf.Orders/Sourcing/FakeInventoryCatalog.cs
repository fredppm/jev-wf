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
}
