namespace JevWf.Orders.Models;

// Declared in progress order on purpose: OrderStatusAggregator uses the enum's numeric order to
// find the "bottleneck" item (the earliest non-terminal stage among all items) without a
// separate priority table. Branches off the happy path (exceptions, returns) are placed after
// the happy-path stage they interrupt, not interleaved - see the comments per group below.
public enum ItemStatus
{
    // ---- Intake / gating (per item, since seller and payment approval can differ per item) ----
    Pending,
    AwaitingSellerConfirmation,
    AwaitingPaymentApproval,            // external event: PaymentApprovedEvent/PaymentDeniedEvent (per seller)

    // ---- Classification-driven gates ----
    AwaitingBlockingRequirementValidation, // generic: prescription, age verification, export license...
    AwaitingManualReview,                  // classification confidence was below the escalation threshold

    // ---- Sourcing ----
    Reserved,                            // sourcing found a stocked origin, stock reserved
    Deferred,                            // no candidate origin had stock - external event: RestockEvent

    // ---- Fulfillment ----
    AwaitingCancellationWindow,
    ReadyForHandling,
    Handling,
    HandlingException,                   // external event: HandlingExceptionEvent - routes back to Deferred or Cancelled
    VerifyingInvoice,
    Invoiced,
    Shipped,
    Delivered,                            // external event: CarrierDeliveredEvent

    // ---- Post-delivery (only reachable after Delivered) ----
    ReturnRequested,                      // external event: ReturnRequestedEvent
    Returned,
    Refunded,

    // ---- Cancellation (reachable from any non-terminal stage above) ----
    CancellationRequested,
    Cancelling,
    Cancelled
}
