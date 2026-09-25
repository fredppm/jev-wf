namespace JevWf.Workflows.Catalog;

// The VTEX pieces plus each seller's custom pieces. Everything is validated at load time, so a
// broken custom piece fails here and never reaches workflow building.
public sealed class PieceCatalog
{
    public const string VtexSource = "vtex";

    private readonly Dictionary<string, IReadOnlyList<PieceDefinition>> _sellers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PieceDefinition> _vtexByName;

    public PieceCatalog(
        TypeRegistry types,
        IReadOnlyList<PieceDefinition> vtex,
        IReadOnlyDictionary<string, IReadOnlyList<PieceDefinition>>? sellers = null)
    {
        Types = types;
        Vtex = vtex;
        _vtexByName = ValidateVtex();

        foreach (var (sellerId, pieces) in sellers ?? new Dictionary<string, IReadOnlyList<PieceDefinition>>())
            _sellers[sellerId] = ComposeSeller(sellerId, pieces);
    }

    public TypeRegistry Types { get; }

    public IReadOnlyList<PieceDefinition> Vtex { get; }

    // The pieces a seller's orders are built from, in catalog order (which is also the order in
    // which pieces plugged in at the same point run).
    public IReadOnlyList<PieceDefinition> For(string sellerId) =>
        _sellers.GetValueOrDefault(sellerId) ?? Vtex;

    // Reads <directory>/vtex.json and every <directory>/sellers/<sellerId>.json.
    public static PieceCatalog Load(string directory)
    {
        var vtex = JsonDefaults.Read<VtexCatalogFile>(Path.Combine(directory, "vtex.json"));

        var sellersDirectory = Path.Combine(directory, "sellers");
        var sellers = Directory.Exists(sellersDirectory)
            ? Directory.GetFiles(sellersDirectory, "*.json")
                .Order(StringComparer.Ordinal)
                .ToDictionary(
                    file => Path.GetFileNameWithoutExtension(file),
                    file => (IReadOnlyList<PieceDefinition>)JsonDefaults.Read<SellerCatalogFile>(file).Pieces)
            : [];

        return new PieceCatalog(vtex.Types, vtex.Pieces, sellers);
    }

    private Dictionary<string, PieceDefinition> ValidateVtex()
    {
        var byName = new Dictionary<string, PieceDefinition>(StringComparer.Ordinal);
        foreach (var piece in Vtex)
        {
            if (!byName.TryAdd(piece.Name, piece))
                throw Invalid(VtexSource, $"piece name '{piece.Name}' is used twice.");
            ValidateShape(VtexSource, piece);
            if (piece.Replaces is not null)
                throw Invalid(VtexSource, $"'{piece.Name}' cannot replace other pieces.");
            if (string.IsNullOrWhiteSpace(piece.PublicStatus))
                throw Invalid(VtexSource, $"'{piece.Name}' has no publicStatus.");
        }

        if (!Vtex.Any(p => p.Scope == PieceScope.Order && p.In.Contains(Types.Start)))
            throw Invalid(VtexSource, $"no order piece accepts the start type '{Types.Start}'.");

        return byName;
    }

    private IReadOnlyList<PieceDefinition> ComposeSeller(string sellerId, IReadOnlyList<PieceDefinition> pieces)
    {
        var names = new HashSet<string>(_vtexByName.Keys, StringComparer.Ordinal);
        foreach (var piece in pieces)
        {
            if (!names.Add(piece.Name))
                throw Invalid(sellerId, $"piece name '{piece.Name}' is already used.");
            ValidateShape(sellerId, piece);
        }

        var chains = pieces
            .Where(p => p.Replaces is not null)
            .GroupBy(p => p.Replaces!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => ComposeChain(sellerId, g.Key, [.. g]), StringComparer.Ordinal);

        var publicStatuses = Vtex.Select(p => p.PublicStatus!).ToHashSet(StringComparer.Ordinal);
        var additions = pieces
            .Where(p => p.Replaces is null)
            .Select(p => publicStatuses.Contains(p.PublicStatus ?? "")
                ? p with { Source = sellerId }
                : throw Invalid(sellerId, $"'{p.Name}' must use one of the VTEX public statuses: {string.Join(", ", publicStatuses)}."));

        var composed = new List<PieceDefinition>();
        foreach (var piece in Vtex)
        {
            if (chains.TryGetValue(piece.Name, out var chain))
                composed.AddRange(chain);
            else
                composed.Add(piece);
        }
        composed.AddRange(additions);
        return composed;
    }

    // A chain replaces a VTEX piece when, seen from outside, it is the same piece: it accepts the
    // same types, leaves through the same types and fails through the same exception. Inside, the
    // types are free. Unhandled exceptions inside the chain leave through the original exception.
    private IReadOnlyList<PieceDefinition> ComposeChain(string sellerId, string targetName, IReadOnlyList<PieceDefinition> chain)
    {
        if (!_vtexByName.TryGetValue(targetName, out var target))
            throw Invalid(sellerId, $"'{chain[0].Name}' replaces unknown VTEX piece '{targetName}'.");

        var targetException = target.OnException ?? Types.DefaultException;
        var accepted = chain.SelectMany(p => p.In).ToHashSet(StringComparer.Ordinal);
        var internalTypes = accepted.Except(target.In).ToHashSet(StringComparer.Ordinal);

        var missing = target.In.Except(accepted).ToList();
        if (missing.Count > 0)
            throw Invalid(sellerId, $"the chain replacing '{targetName}' must accept [{string.Join(", ", missing)}].");

        var exits = chain.SelectMany(p => p.Out.Values).Where(t => !internalTypes.Contains(t)).ToHashSet(StringComparer.Ordinal);
        if (!exits.SetEquals(target.Out.Values))
            throw Invalid(sellerId,
                $"the chain replacing '{targetName}' leaves through [{string.Join(", ", exits.Order())}] " +
                $"but '{targetName}' leaves through [{string.Join(", ", target.Out.Values.Distinct().Order())}].");

        return chain.Select(piece =>
        {
            if (piece.Scope != target.Scope)
                throw Invalid(sellerId, $"'{piece.Name}' must have the same scope as '{targetName}' ({target.Scope}).");
            if (piece.OnException is { } exception && exception != targetException && !internalTypes.Contains(exception))
                throw Invalid(sellerId, $"'{piece.Name}' fails through '{exception}', but the chain replacing '{targetName}' can only fail through '{targetException}'.");

            // The entry of the chain takes over the replaced piece's kind and condition.
            var isEntry = piece.In.Any(target.In.Contains);
            return piece with
            {
                Source = sellerId,
                Kind = isEntry ? target.Kind : PieceKind.Fixed,
                When = isEntry ? target.When : null,
                OnException = piece.OnException ?? targetException,
                PublicStatus = target.PublicStatus
            };
        }).ToList();
    }

    private void ValidateShape(string source, PieceDefinition piece)
    {
        if (piece.In.Count == 0)
            throw Invalid(source, $"'{piece.Name}' accepts no type.");
        if (piece.Out.Count == 0)
            throw Invalid(source, $"'{piece.Name}' has no output.");
        if (piece.Out.ContainsKey(PieceDefinition.ExceptionPort))
            throw Invalid(source, $"'{piece.Name}' cannot name an output '{PieceDefinition.ExceptionPort}'; use onException.");
        if (piece.In.FirstOrDefault(Types.IsTerminal) is { } terminal)
            throw Invalid(source, $"'{piece.Name}' accepts the terminal type '{terminal}'.");
        if (piece.When is { Jev: not null, JevNot: not null })
            throw Invalid(source, $"'{piece.Name}' cannot have both 'jev' and 'jevNot'.");
        if (piece.Kind == PieceKind.Required && piece.When is null && piece.Replaces is null)
            throw Invalid(source, $"'{piece.Name}' is required but has no condition; use kind 'fixed'.");
    }

    private static InvalidDataException Invalid(string source, string message) =>
        new($"Catalog '{source}': {message}");

    private sealed record VtexCatalogFile(TypeRegistry Types, List<PieceDefinition> Pieces);

    private sealed record SellerCatalogFile(List<PieceDefinition> Pieces);
}
