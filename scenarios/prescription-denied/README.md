# prescription-denied

> A prescription gets denied - that item is cancelled before sourcing runs, while the rest of the order ships normally.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `deniedmed1` | seller-pharmacy-bh | Controlled anxiolytic medication, requires a medical prescription to be presented. | 1 |
| `other1` | seller-stationery-bh | Ordinary paper notebook, no special handling, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `deniedmed1` | seller-pharmacy-bh-dc | 5 | 2 |
| `other1` | seller-stationery-bh-wh | 10 | 5 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-pharmacy-bh
2. `PaymentApprovedEvent` sellerId=seller-stationery-bh
3. `FinalizeEvent`
4. `CarrierDeliveredEvent` itemId=other1
5. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `deniedmed1` | prescription | - | standard | - | **Cancelled** |
| `other1` | none | - | standard | seller-stationery-bh-wh | **Delivered** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| seller-stationery-bh-wh | standard | `other1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(deniedmed1, seller=seller-pharmacy-bh) -> confirmed
await_payment_approval(deniedmed1, seller=seller-pharmacy-bh)
request_seller_confirmation(other1, seller=seller-stationery-bh) -> confirmed
await_payment_approval(other1, seller=seller-stationery-bh)
request_blocking_requirement_validation(deniedmed1, type=prescription, attachment-deniedmed1) -> denied
cancel_item(deniedmed1, "prescription not validated")
set_shipping_method(other1, standard)
reserve_item_stock(other1, origin=seller-stationery-bh-wh, qty=1)
start_fulfillment(other1)
create_shipment(order-eval-blocking-requirement-denied, origin=seller-stationery-bh-wh, method=standard, items=[other1])
mark_item_shipped(other1)
notify_customer(order-eval-blocking-requirement-denied, "Your order has been confirmed and is on its way.")
mark_item_delivered(other1)
close_order(order-eval-blocking-requirement-denied)
```
</details>
