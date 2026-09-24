# handling-exception-reroutes-to-resourcing

> Item is damaged during packing at its first-choice origin - re-sourcing reroutes it to the next-best origin instead.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `vase1` | seller-fragile-goods | Ordinary ceramic vase, physical product, no special shipping care required, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `vase1` | fragile-origin-a-sp | 5 | 5 |
| `vase1` | fragile-origin-b-rj | 5 | 20 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-fragile-goods
2. `HandlingExceptionEvent` itemId=vase1, reason=damaged during packing
3. `RestockEvent` itemId=vase1, originId=fragile-origin-a-sp, newAvailableStock=0
4. `FinalizeEvent`
5. `CarrierDeliveredEvent` itemId=vase1
6. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `vase1` | none | - | standard | fragile-origin-b-rj | **Delivered** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| fragile-origin-b-rj | standard | `vase1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(vase1, seller=seller-fragile-goods) -> confirmed
await_payment_approval(vase1, seller=seller-fragile-goods)
set_shipping_method(vase1, standard)
reserve_item_stock(vase1, origin=fragile-origin-a-sp, qty=1)
start_fulfillment(vase1)
handle_handling_exception(vase1, "damaged during packing")
defer_item(vase1, "handling exception: damaged during packing")
reserve_item_stock(vase1, origin=fragile-origin-b-rj, qty=1)
start_fulfillment(vase1)
create_shipment(order-eval-handling-exception, origin=fragile-origin-b-rj, method=standard, items=[vase1])
mark_item_shipped(vase1)
notify_customer(order-eval-handling-exception, "Your order has been confirmed and is on its way.")
mark_item_delivered(vase1)
close_order(order-eval-handling-exception)
```
</details>
