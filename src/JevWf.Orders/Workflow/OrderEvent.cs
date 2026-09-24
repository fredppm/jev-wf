namespace JevWf.Orders.Workflow;

// External triggers OrderWorkflowRunner.HandleEventAsync reacts to. Everything that can't be decided
// synchronously in one pass - payment clearing, a warehouse restocking, a carrier scan, damage
// during packing, a return request - arrives as one of these instead of being assumed to happen
// instantly.
public abstract record OrderEvent;

public sealed record PaymentApprovedEvent(string SellerId) : OrderEvent;

public sealed record PaymentDeniedEvent(string SellerId) : OrderEvent;

// Origin now has stock for an item that was previously Deferred - re-run sourcing for just it.
public sealed record RestockEvent(string ItemId, string OriginId, int NewAvailableStock) : OrderEvent;

public sealed record HandlingExceptionEvent(string ItemId, string Reason) : OrderEvent;

public sealed record CarrierDeliveredEvent(string ItemId) : OrderEvent;

public sealed record ReturnRequestedEvent(string ItemId, string Reason) : OrderEvent;

// Not a real external trigger - it's "check what's ready and ship/close it now". Modeled as an
// event (rather than only a directly-callable method) so a caller that drives the workflow from
// a flat, ordered event list can control exactly when shipment grouping happens relative to
// other events, e.g. before a CarrierDeliveredEvent that needs the item already Shipped.
public sealed record FinalizeEvent : OrderEvent;
