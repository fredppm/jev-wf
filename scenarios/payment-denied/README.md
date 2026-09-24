# payment-denied

> Payment never clears for the item's seller - cancelled before any classification gate or sourcing runs.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `gadget1` | seller-flaky-gadgets | Ordinary bluetooth speaker, physical product, no special handling, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `gadget1` | seller-flaky-gadgets-wh-sp | 10 | 5 |

**Events** (in order)

1. `PaymentDeniedEvent` sellerId=seller-flaky-gadgets
2. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `gadget1` | none | - | standard | - | **Cancelled** |

**Shipments** (grouped by origin + SLA)

_none_

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(gadget1, seller=seller-flaky-gadgets) -> confirmed
await_payment_approval(gadget1, seller=seller-flaky-gadgets)
cancel_item(gadget1, "payment denied")
close_order(order-eval-payment-denied)
```
</details>
