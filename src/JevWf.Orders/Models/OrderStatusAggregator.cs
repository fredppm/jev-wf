namespace JevWf.Orders.Models;

// The order has no state machine of its own - its status is always derived from its items'.
// This is a display/reporting aggregate only; nothing in the workflow reads it back.
public static class OrderStatusAggregator
{
    private static readonly HashSet<ItemStatus> TerminalSuccess = new()
    {
        ItemStatus.Invoiced, ItemStatus.Shipped, ItemStatus.Delivered, ItemStatus.Returned, ItemStatus.Refunded
    };

    private static readonly HashSet<ItemStatus> TerminalCancelled = new() { ItemStatus.Cancelled };

    // Returns either "Cancelled", "Invoiced", "PartiallyInvoiced", or the ItemStatus name of the
    // least-advanced item (the bottleneck holding the rest of the order back).
    public static string Compute(Order order)
    {
        var statuses = order.Items.Select(i => i.Status).ToList();

        if (statuses.Count > 0 && statuses.All(s => TerminalCancelled.Contains(s)))
            return "Cancelled";

        if (statuses.All(s => TerminalSuccess.Contains(s) || TerminalCancelled.Contains(s)))
        {
            var hasSuccess = statuses.Any(s => TerminalSuccess.Contains(s));
            var hasCancelled = statuses.Any(s => TerminalCancelled.Contains(s));
            return hasSuccess && hasCancelled ? "PartiallyInvoiced" : "Invoiced";
        }

        var bottleneck = statuses
            .Where(s => !TerminalSuccess.Contains(s) && !TerminalCancelled.Contains(s))
            .Min();

        return bottleneck.ToString();
    }
}
