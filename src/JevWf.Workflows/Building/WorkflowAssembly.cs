using JevWf.Workflows.Catalog;
using JevWf.Workflows.Orders;
using JevWf.Workflows.Output;

namespace JevWf.Workflows.Building;

// Connects the active pieces of one order into a graph, starting at the start type and following
// every output and exception until each flow reaches a terminal type. Deterministic: the same
// decisions always give the same graph. Problems are collected in Errors instead of thrown.
internal sealed class WorkflowAssembly
{
    private readonly TypeRegistry _types;
    private readonly Order _order;
    private readonly WorkflowMode _mode;
    private readonly Decisions _decisions;
    private readonly IReadOnlyList<Instance> _items;
    private readonly Dictionary<string, ActivePieces> _active = new(StringComparer.Ordinal);
    private readonly List<WorkflowNode> _nodes = [];
    private readonly HashSet<string> _nodeIds = new(StringComparer.Ordinal);
    private readonly List<WorkflowEdge> _edges = [];
    private readonly List<string> _errors = [];
    private readonly string? _start;

    public WorkflowAssembly(
        TypeRegistry types,
        IReadOnlyList<PieceDefinition> pieces,
        Order order,
        IReadOnlyList<Instance> instances,
        Decisions decisions,
        WorkflowMode mode)
    {
        _types = types;
        _order = order;
        _mode = mode;
        _decisions = decisions;
        _items = instances.Where(i => i.Scope == PieceScope.Item).ToList();

        foreach (var instance in instances)
        {
            var active = decisions.Active[instance.Key];
            _active[instance.Key] = new ActivePieces(
                pieces.Where(p => p.Scope == instance.Scope && active.Contains(p.Name)).ToList(),
                ActivePieces.EntryTypes(pieces, types, instance.Scope));
        }

        var orderInstance = instances.Single(i => i.Scope == PieceScope.Order);
        var start = Resolve(orderInstance, types.Start, from: null);
        if (start is [{ } startId])
            _start = startId;
        else
            _errors.Add($"The start type '{types.Start}' does not lead to a single order piece.");

        Validate(instances);
    }

    public IReadOnlyList<string> Errors => _errors;

    // Edges are listed in the order their source nodes were placed, from the start onwards.
    public OrderWorkflow ToWorkflow()
    {
        if (_errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", _errors));

        var position = _nodes.Select((node, index) => (node.Id, index)).ToDictionary(n => n.Id, n => n.index, StringComparer.Ordinal);
        return new OrderWorkflow(_order.OrderId, _mode, _start!, _nodes, [.. _edges.OrderBy(e => position[e.From])]);
    }

    // The nodes a flow holding `type` goes to next (null = the flow ends there).
    private List<string?> Resolve(Instance instance, string type, PieceDefinition? from)
    {
        if (_types.IsTerminal(type))
            return [null];

        var sequence = _active[instance.Key].SequenceFor(type);

        // An order type nothing at order level accepts splits into one flow per item.
        if (sequence.Gates.Count == 0 && sequence.Advancing.Count == 0)
        {
            if (instance.Scope == PieceScope.Order && _items.Count > 0)
                return _items.SelectMany(item => Resolve(item, type, from: null)).ToList();

            _errors.Add($"Nothing accepts '{type}' for {Describe(instance)}.");
            return [];
        }

        // Coming back from a gate's chain: go to the next gate, otherwise start from the first.
        var next = 0;
        if (from is not null)
        {
            var owner = sequence.Gates.ToList().FindIndex(g => g.Members.Contains(from.Name));
            if (owner >= 0)
                next = owner + 1;
        }

        if (next < sequence.Gates.Count)
            return [Place(instance, sequence.Gates[next].Head)];

        if (sequence.Advancing.Count == 0)
        {
            _errors.Add($"Nothing moves '{type}' forward for {Describe(instance)}.");
            return [];
        }

        return [Place(instance, Choose(instance, type, sequence.Advancing))];
    }

    private string Place(Instance instance, PieceDefinition piece)
    {
        var id = instance.NodeId(piece.Name);
        if (!_nodeIds.Add(id))
            return id;

        _nodes.Add(new WorkflowNode(id, piece.Name, piece.Scope, instance.Item?.ItemId, piece.PublicStatus!, piece.Config?.DeepClone().AsObject()));

        foreach (var (port, type) in piece.Out)
            Connect(instance, piece, id, port, type);
        Connect(instance, piece, id, PieceDefinition.ExceptionPort, piece.OnException ?? _types.DefaultException);

        return id;
    }

    private void Connect(Instance instance, PieceDefinition piece, string id, string port, string type)
    {
        foreach (var target in Resolve(instance, type, piece))
            _edges.Add(new WorkflowEdge(id, port, type, target));
    }

    private PieceDefinition Choose(Instance instance, string type, IReadOnlyList<PieceDefinition> candidates)
    {
        if (_decisions.Choices.TryGetValue((instance.Key, type), out var chosen)
            && candidates.FirstOrDefault(c => c.Name == chosen) is { } piece)
            return piece;

        return candidates.FirstOrDefault(c => c.Default) ?? candidates[0];
    }

    private void Validate(IReadOnlyList<Instance> instances)
    {
        foreach (var instance in instances)
        {
            foreach (var required in _active[instance.Key].Pieces.Where(p => p.Kind == PieceKind.Required))
            {
                if (!_nodeIds.Contains(instance.NodeId(required.Name)))
                    _errors.Add($"Required piece '{required.Name}' is not reachable for {Describe(instance)}.");
            }
        }

        // Every node must have a way to finish.
        var incoming = _edges.Where(e => e.To is not null).ToLookup(e => e.To!, e => e.From);
        var finishing = new HashSet<string>(_edges.Where(e => e.To is null).Select(e => e.From), StringComparer.Ordinal);
        var pending = new Queue<string>(finishing);
        while (pending.TryDequeue(out var node))
        {
            foreach (var source in incoming[node])
            {
                if (finishing.Add(source))
                    pending.Enqueue(source);
            }
        }

        foreach (var node in _nodes.Where(n => !finishing.Contains(n.Id)))
            _errors.Add($"'{node.Id}' can never reach a terminal type.");
    }

    private string Describe(Instance instance) =>
        instance.Item is null ? $"order '{_order.OrderId}'" : $"item '{instance.Item.ItemId}'";
}
