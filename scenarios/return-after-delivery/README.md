# return-after-delivery

> A delivered item is returned - the one post-delivery path (Returned + refund) no other scenario reaches.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `watch1` | seller-timepieces | Ordinary wristwatch, physical product, no special handling, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `watch1` | timepieces-wh-sp | 5 | 6 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-timepieces
2. `FinalizeEvent`
3. `CarrierDeliveredEvent` itemId=watch1
4. `ReturnRequestedEvent` itemId=watch1, reason=customer changed their mind
5. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `watch1` | none | - | standard | timepieces-wh-sp | **Returned** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| timepieces-wh-sp | standard | `watch1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(watch1, seller=seller-timepieces) -> confirmed
await_payment_approval(watch1, seller=seller-timepieces)
[jev] watch1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.06)
[jev] watch1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(watch1, standard)
reserve_item_stock(watch1, origin=timepieces-wh-sp, qty=1)
start_fulfillment(watch1)
create_shipment(order-eval-return, origin=timepieces-wh-sp, method=standard, items=[watch1])
mark_item_shipped(watch1)
notify_customer(order-eval-return, "Your order has been confirmed and is on its way.")
mark_item_delivered(watch1)
request_return(watch1, "customer changed their mind")
mark_item_returned(watch1)
refund_order(order-eval-return, item=watch1, "customer changed their mind")
close_order(order-eval-return)
```
</details>
