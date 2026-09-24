# restock-reopens-deferred-item

> Out of stock everywhere at first (Deferred), then a RestockEvent lets sourcing find an origin and the item ships.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `toy2` | seller-toystore | Ordinary wooden toy, physical product, no special handling, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `toy2` | toystore-wh-sp | 0 | 4 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-toystore
2. `FinalizeEvent`
3. `RestockEvent` itemId=toy2, originId=toystore-wh-sp, newAvailableStock=10
4. `FinalizeEvent`
5. `CarrierDeliveredEvent` itemId=toy2
6. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `toy2` | none | - | standard | toystore-wh-sp | **Delivered** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| toystore-wh-sp | standard | `toy2` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(toy2, seller=seller-toystore) -> confirmed
await_payment_approval(toy2, seller=seller-toystore)
[jev] toy2: options=[mark_item_digital_only, request_blocking_requirement_validation, escalate_for_manual_review, reserve_item_stock, defer_item, cancel_item] -> defer_item (confidence 1.00)
defer_item(toy2, "payment approved by seller-toystore")
notify_customer(order-eval-restock, "Your order shipped partially. Item(s) toy2 are awaiting restock.")
[jev] toy2: options=[reserve_item_stock, defer_item, cancel_item] -> reserve_item_stock (confidence 0.87)
set_shipping_method(toy2, standard)
reserve_item_stock(toy2, origin=toystore-wh-sp, qty=1)
start_fulfillment(toy2)
create_shipment(order-eval-restock, origin=toystore-wh-sp, method=standard, items=[toy2])
mark_item_shipped(toy2)
notify_customer(order-eval-restock, "Your order has been confirmed and is on its way.")
mark_item_delivered(toy2)
close_order(order-eval-restock)
```
</details>
