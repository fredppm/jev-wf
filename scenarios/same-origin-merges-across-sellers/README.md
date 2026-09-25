# same-origin-merges-across-sellers

> Two items from different sellers that route through the same origin+SLA must merge into one shipment.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `book1` | seller-books | Ordinary printed book, no special handling, no delivery urgency, no legal restriction. | 1 |
| `toy1` | seller-toys | Ordinary toy, no special handling, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `book1` | shared-3pl-hub-campinas | 20 | 8 |
| `toy1` | shared-3pl-hub-campinas | 15 | 8 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-books
2. `PaymentApprovedEvent` sellerId=seller-toys
3. `FinalizeEvent`
4. `CarrierDeliveredEvent` itemId=book1
5. `CarrierDeliveredEvent` itemId=toy1
6. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `book1` | none | - | standard | shared-3pl-hub-campinas | **Delivered** |
| `toy1` | none | - | standard | shared-3pl-hub-campinas | **Delivered** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| shared-3pl-hub-campinas | standard | `book1`, `toy1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(book1, seller=seller-books) -> confirmed
await_payment_approval(book1, seller=seller-books)
request_seller_confirmation(toy1, seller=seller-toys) -> confirmed
await_payment_approval(toy1, seller=seller-toys)
[jev] book1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.05)
[jev] book1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(book1, standard)
reserve_item_stock(book1, origin=shared-3pl-hub-campinas, qty=1)
start_fulfillment(book1)
[jev] toy1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.06)
[jev] toy1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(toy1, standard)
reserve_item_stock(toy1, origin=shared-3pl-hub-campinas, qty=1)
start_fulfillment(toy1)
create_shipment(order-eval-same-origin-merge, origin=shared-3pl-hub-campinas, method=standard, items=[book1, toy1])
mark_item_shipped(book1)
mark_item_shipped(toy1)
notify_customer(order-eval-same-origin-merge, "Your order has been confirmed and is on its way.")
mark_item_delivered(book1)
mark_item_delivered(toy1)
close_order(order-eval-same-origin-merge)
```
</details>
