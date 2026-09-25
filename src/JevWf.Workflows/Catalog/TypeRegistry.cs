namespace JevWf.Workflows.Catalog;

// The fixed types of the VTEX catalog: where every workflow starts, where flows end (with the
// public status they end in) and where an unhandled exception goes.
public sealed record TypeRegistry(
    string Start,
    IReadOnlyDictionary<string, string> Terminals,
    string DefaultException)
{
    public bool IsTerminal(string type) => Terminals.ContainsKey(type);
}
