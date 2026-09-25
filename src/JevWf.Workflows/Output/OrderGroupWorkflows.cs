using System.Text.Json.Nodes;
using JevWf.Workflows.Catalog;

namespace JevWf.Workflows.Output;

// The result: one workflow per order. Only pieces and how they connect, no order data.
public sealed record OrderGroupWorkflows(string OrderGroupId, IReadOnlyList<OrderWorkflow> Workflows);

public sealed record OrderWorkflow(
    string OrderId,
    WorkflowMode Mode,
    string Start,
    IReadOnlyList<WorkflowNode> Nodes,
    IReadOnlyList<WorkflowEdge> Edges);

public enum WorkflowMode
{
    // Built from the Jev's decisions.
    Jev,
    // The Jev was not confident (or its workflow was invalid): the complete default workflow.
    Default
}

public sealed record WorkflowNode(
    string Id,
    string Piece,
    PieceScope Scope,
    string? ItemId,
    string PublicStatus,
    JsonObject? Config);

// An edge without "to" ends the flow at a terminal type.
public sealed record WorkflowEdge(string From, string Port, string Type, string? To);
