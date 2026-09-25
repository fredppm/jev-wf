# custom-tool-fraud-check

> New tool defined only in JSON (fraud_check): the high-value item gets checked and cancelled when rejected; the cheap item skips the check and ships.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `goldwatch1` | seller-jewelry | Luxury 18k gold wristwatch, ordinary physical product, no special shipping care, no delivery urgency, no legal restriction. | 1 |
| `socks1` | seller-apparel | Ordinary cotton socks, physical product, no special shipping care, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `goldwatch1` | jewelry-vault-sp | 2 | 8 |
| `socks1` | apparel-dc-poa | 50 | 6 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-jewelry
2. `PaymentApprovedEvent` sellerId=seller-apparel
3. `FinalizeEvent`
4. `CarrierDeliveredEvent` itemId=socks1
5. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `goldwatch1` | none | - | standard | - | **Cancelled** |
| `socks1` | none | - | standard | apparel-dc-poa | **Delivered** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| apparel-dc-poa | standard | `socks1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(goldwatch1, seller=seller-jewelry) -> confirmed
await_payment_approval(goldwatch1, seller=seller-jewelry)
request_seller_confirmation(socks1, seller=seller-apparel) -> confirmed
await_payment_approval(socks1, seller=seller-apparel)
[jev] goldwatch1: checks escalate_for_manual_review=no (0.04), fraud_check=yes (0.98)
fraud_check(goldwatch1) -> rejected
[jev] goldwatch1: checks escalate_for_manual_review=no (0.03)
[jev] goldwatch1: options=[cancel_item, defer_item] -> cancel_item (confidence 1.00)
cancel_item(goldwatch1, "failed checks: fraud_check=rejected")
[jev] socks1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.02)
[jev] socks1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(socks1, standard)
reserve_item_stock(socks1, origin=apparel-dc-poa, qty=1)
start_fulfillment(socks1)
create_shipment(order-eval-fraud-check, origin=apparel-dc-poa, method=standard, items=[socks1])
mark_item_shipped(socks1)
notify_customer(order-eval-fraud-check, "Your order has been confirmed and is on its way.")
mark_item_delivered(socks1)
close_order(order-eval-fraud-check)
```
</details>
