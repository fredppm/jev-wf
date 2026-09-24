namespace JevWf.Orders.Models;

public enum ItemStatus
{
    Pending,
    Reserved,
    AwaitingPrescriptionValidation,
    InFulfillment,
    Shipped,
    Delivered,
    Deferred,
    Cancelled
}
