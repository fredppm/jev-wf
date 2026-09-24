namespace JevWf.Orders.Tools;

// Catalog of actions the order workflow can execute. The Jev never sees this catalog:
// it answers Choice/Score/Noul questions about the order, and OrderWorkflowRunner combines
// those answers to decide which of these actions to trigger.
public static class OrderToolCatalog
{
    public static IReadOnlyList<ToolDefinition> All { get; } = new[]
    {
        // ---- Item-level tools: intake / gating ----
        new ToolDefinition("request_seller_confirmation", "Asks the item's seller to confirm they can fulfill it.", ToolLevel.Item),
        new ToolDefinition("await_payment_approval", "Marks an item as blocked on that seller's payment clearing.", ToolLevel.Item),
        new ToolDefinition("request_blocking_requirement_validation", "Sends the item's blocking-requirement evidence (prescription, age verification, ...) for validation.", ToolLevel.Item),
        new ToolDefinition("escalate_for_manual_review", "Sends a low-confidence classification result to a human reviewer.", ToolLevel.Item),

        // ---- Item-level tools: sourcing / fulfillment ----
        new ToolDefinition("reserve_item_stock", "Reserves the item's quantity at its chosen sourcing origin.", ToolLevel.Item),
        new ToolDefinition("set_shipping_method", "Sets the shipping method/SLA for an item (standard, express, refrigerated).", ToolLevel.Item),
        new ToolDefinition("mark_item_digital_only", "Marks an item as fully virtual, with no physical fulfillment step.", ToolLevel.Item),
        new ToolDefinition("start_fulfillment", "Triggers physical fulfillment (picking/packing) for an already-reserved item.", ToolLevel.Item),
        new ToolDefinition("handle_handling_exception", "Records a fulfillment-time problem (e.g. damaged in packing) for an item.", ToolLevel.Item),
        new ToolDefinition("mark_item_shipped", "Marks an item as handed off to the carrier.", ToolLevel.Item),
        new ToolDefinition("mark_item_delivered", "Marks an item as delivered, from a carrier tracking event.", ToolLevel.Item),

        // ---- Item-level tools: post-delivery / terminal ----
        new ToolDefinition("request_return", "Opens a return for a delivered item.", ToolLevel.Item),
        new ToolDefinition("mark_item_returned", "Marks a returned item as received back.", ToolLevel.Item),
        new ToolDefinition("complete_item", "Closes an item as completed (used for digital items, delivered instantly).", ToolLevel.Item),
        new ToolDefinition("defer_item", "Puts an item on hold pending a future event (restock, exception recovery), without cancelling it.", ToolLevel.Item),
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
