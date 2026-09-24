namespace JevWf.Orders.Tools;

// An action the workflow (deterministic code) can trigger. The Jev never invokes
// this directly — it only answers questions; OrderWorkflowRunner decides which
// action to call based on the answers.
public sealed record ToolDefinition(
    string Name,
    string Description,
    ToolLevel Level);
