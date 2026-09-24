namespace JevWf.Orders.Tools;

// Catalog of actions the order workflow can execute. The Jev never sees this catalog:
// it answers Choice/Score/Noul questions about the order, and OrderWorkflowRunner combines
// those answers to decide which of these actions to trigger.
public static class OrderToolCatalog
{
    public static IReadOnlyList<ToolDefinition> All { get; } = new[]
    {
        // ---- Item-level tools ----
        new ToolDefinition("reserve_item_stock", "Reserves the item's quantity at its chosen sourcing origin.", ToolLevel.Item),
        new ToolDefinition("start_fulfillment", "Triggers physical fulfillment for an already-reserved item.", ToolLevel.Item),
        new ToolDefinition("request_prescription_validation", "Sends the item's attached prescription for validation.", ToolLevel.Item),
        new ToolDefinition("set_shipping_method", "Sets the shipping method/SLA for an item (standard, express, refrigerated).", ToolLevel.Item),
        new ToolDefinition("mark_item_digital_only", "Marks an item as fully virtual, with no physical fulfillment step.", ToolLevel.Item),
        new ToolDefinition("complete_item", "Closes an item as completed.", ToolLevel.Item),
        new ToolDefinition("defer_item", "Puts an item on hold pending a future event, without cancelling it.", ToolLevel.Item),
        new ToolDefinition("cancel_item", "Cancels a specific order item.", ToolLevel.Item),

        // ---- Order-level tools ----
        new ToolDefinition("get_order", "Fetches the full order snapshot.", ToolLevel.Order),
        new ToolDefinition("create_shipment", "Groups reserved items sharing the same origin and shipping method into a shipment and sends it.", ToolLevel.Order),
        new ToolDefinition("notify_customer", "Sends the customer a notification about the order.", ToolLevel.Order),
        new ToolDefinition("hold_order", "Pauses the entire order.", ToolLevel.Order),
        new ToolDefinition("refund_order", "Issues a full or partial refund.", ToolLevel.Order),
        new ToolDefinition("close_order", "Closes the order once every item is in a terminal state.", ToolLevel.Order),
    };

    public static IReadOnlyList<ToolDefinition> ItemLevel =>
        All.Where(t => t.Level == ToolLevel.Item).ToList();

    public static IReadOnlyList<ToolDefinition> OrderLevel =>
        All.Where(t => t.Level == ToolLevel.Order).ToList();
}
