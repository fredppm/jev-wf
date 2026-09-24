# simple-shirt

> One item, one seller, standard shipping. Trivial baseline, all the way to Delivered.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `shirt1` | seller-apparel | Ordinary cotton t-shirt, no special handling, no legal restriction, not digital. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `shirt1` | seller-apparel-dc-sp | 10 | 6 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-apparel
2. `FinalizeEvent`
3. `CarrierDeliveredEvent` itemId=shirt1
4. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `shirt1` | none | - | standard | seller-apparel-dc-sp | **Delivered** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| seller-apparel-dc-sp | standard | `shirt1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(shirt1, seller=seller-apparel) -> confirmed
await_payment_approval(shirt1, seller=seller-apparel)
[jev] shirt1: options=[mark_item_digital_only, request_blocking_requirement_validation, escalate_for_manual_review, reserve_item_stock, defer_item, cancel_item] -> reserve_item_stock (confidence 1.00)
set_shipping_method(shirt1, standard)
reserve_item_stock(shirt1, origin=seller-apparel-dc-sp, qty=1)
start_fulfillment(shirt1)
create_shipment(order-eval-simple-shirt, origin=seller-apparel-dc-sp, method=standard, items=[shirt1])
mark_item_shipped(shirt1)
notify_customer(order-eval-simple-shirt, "Your order has been confirmed and is on its way.")
mark_item_delivered(shirt1)
close_order(order-eval-simple-shirt)
```
</details>
