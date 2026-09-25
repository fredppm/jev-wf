using JevWf.Workflows.Catalog;

namespace JevWf.Workflows.Building;

// Where a flow is when it holds a type: an order, or one of its items.
internal sealed record Instance(string Key, PieceScope Scope, Orders.OrderItem? Item)
{
    public const string OrderKey = "order";

    public string NodeId(string piece) => Item is null ? piece : $"{Item.ItemId}/{piece}";

    public static IReadOnlyList<Instance> For(Orders.Order order) =>
    [
        new(OrderKey, PieceScope.Order, null),
        .. order.Items.Select(item => new Instance(item.ItemId, PieceScope.Item, item))
    ];
}

// A piece plugged in at a type: it accepts the type and its chain gives the same type back
// (payment, fraud check, prescription). Members are the pieces of that chain.
internal sealed record Gate(PieceDefinition Head, IReadOnlySet<string> Members);

// What happens to a type: its gates run in catalog order, then one advancing piece moves on.
internal sealed record TypeSequence(IReadOnlyList<Gate> Gates, IReadOnlyList<PieceDefinition> Advancing);

// The active pieces of one instance and how they sit around each type. Entry types reach this
// scope from outside it (the start type, or types the other scope produces).
internal sealed class ActivePieces(IReadOnlyList<PieceDefinition> pieces, IReadOnlySet<string> entryTypes)
{
    public static IReadOnlySet<string> EntryTypes(IReadOnlyList<PieceDefinition> catalog, TypeRegistry types, PieceScope scope) =>
        catalog.Where(p => p.Scope != scope)
            .SelectMany(p => p.Out.Values)
            .Append(types.Start)
            .ToHashSet(StringComparer.Ordinal);

    private readonly Dictionary<string, TypeSequence> _sequences = new(StringComparer.Ordinal);

    public IReadOnlyList<PieceDefinition> Pieces => pieces;

    public IEnumerable<string> AcceptedTypes => pieces.SelectMany(p => p.In).Distinct(StringComparer.Ordinal);

    public TypeSequence SequenceFor(string type)
    {
        if (_sequences.TryGetValue(type, out var cached))
            return cached;

        var gates = new List<Gate>();
        var advancing = new List<PieceDefinition>();
        foreach (var piece in pieces.Where(p => p.In.Contains(type)))
        {
            if (ChainBackTo(piece, type) is { } members)
                gates.Add(new Gate(piece, members));
            else
                advancing.Add(piece);
        }

        return _sequences[type] = new TypeSequence(gates, advancing);
    }

    // A gate sits on a flow that exists without it: the type also comes from outside the chain,
    // and the chain (following regular outputs, not exceptions, through pieces only the chain
    // feeds) gives the type back. Returns the chain, or null when the piece is not a gate. A loop
    // like reserve_stock -> wait_restock -> restock_received is not a gate: nothing else produces
    // restock_received.
    private HashSet<string>? ChainBackTo(PieceDefinition start, string type)
    {
        var members = new HashSet<string>(StringComparer.Ordinal) { start.Name };
        var reached = new HashSet<string>(start.Out.Values, StringComparer.Ordinal);

        var grew = true;
        while (grew)
        {
            if (reached.Contains(type))
                return entryTypes.Contains(type) || ProducersOf(type).Any(p => !members.Contains(p)) ? members : null;

            grew = false;
            foreach (var piece in pieces)
            {
                if (members.Contains(piece.Name)
                    || !piece.In.Any(reached.Contains)
                    || piece.In.Any(entryTypes.Contains)
                    || !piece.In.All(t => ProducersOf(t).All(members.Contains)))
                    continue;

                members.Add(piece.Name);
                reached.UnionWith(piece.Out.Values);
                grew = true;
            }
        }

        return null;
    }

    private IEnumerable<string> ProducersOf(string type) =>
        pieces.Where(p => p.Out.Values.Contains(type)).Select(p => p.Name);
}
