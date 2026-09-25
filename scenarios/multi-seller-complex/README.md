# multi-seller-complex

> Fridge + ice cream from different sellers/origins/SLAs, plus a third item whose seller is out of stock everywhere.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `fridge1` | seller-appliances | Large major home appliance. Needs careful freight handling but is not perishable, no delivery urgency, no legal restriction. | 1 |
| `icecream1` | seller-frozen-foods | Frozen dessert, perishable, must stay frozen and requires cold-chain fast delivery, no legal restriction. | 1 |
| `collectible1` | seller-collectibles | Decorative collectible item, ordinary physical product, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `fridge1` | seller-appliances-wh-rj | 5 | 48 |
| `icecream1` | seller-frozen-foods-dc-sp | 8 | 3 |
| `collectible1` | seller-collectibles-store-sp | 0 | 2 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-appliances
2. `PaymentApprovedEvent` sellerId=seller-frozen-foods
3. `PaymentApprovedEvent` sellerId=seller-collectibles
4. `FinalizeEvent`
5. `CarrierDeliveredEvent` itemId=fridge1
6. `CarrierDeliveredEvent` itemId=icecream1
7. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `fridge1` | none | - | standard | seller-appliances-wh-rj | **Delivered** |
| `icecream1` | none | - | refrigerated_express | seller-frozen-foods-dc-sp | **Delivered** |
| `collectible1` | none | - | standard | - | **Deferred** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| seller-appliances-wh-rj | standard | `fridge1` |
| seller-frozen-foods-dc-sp | refrigerated_express | `icecream1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(fridge1, seller=seller-appliances) -> confirmed
await_payment_approval(fridge1, seller=seller-appliances)
request_seller_confirmation(icecream1, seller=seller-frozen-foods) -> confirmed
await_payment_approval(icecream1, seller=seller-frozen-foods)
request_seller_confirmation(collectible1, seller=seller-collectibles) -> confirmed
await_payment_approval(collectible1, seller=seller-collectibles)
[jev] fridge1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.13)
[jev] fridge1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(fridge1, standard)
reserve_item_stock(fridge1, origin=seller-appliances-wh-rj, qty=1)
start_fulfillment(fridge1)
[jev] icecream1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.06)
[jev] icecream1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(icecream1, refrigerated_express)
reserve_item_stock(icecream1, origin=seller-frozen-foods-dc-sp, qty=1)
start_fulfillment(icecream1)
[jev] collectible1: checks escalate_for_manual_review=no (0.04), fraud_check=no (0.06)
[jev] collectible1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> defer_item (confidence 1.00)
defer_item(collectible1, "payment approved by seller-collectibles")
create_shipment(order-eval-multiseller, origin=seller-appliances-wh-rj, method=standard, items=[fridge1])
mark_item_shipped(fridge1)
create_shipment(order-eval-multiseller, origin=seller-frozen-foods-dc-sp, method=refrigerated_express, items=[icecream1])
mark_item_shipped(icecream1)
notify_customer(order-eval-multiseller, "Your order shipped partially. Item(s) collectible1 are awaiting restock.")
mark_item_delivered(fridge1)
mark_item_delivered(icecream1)
notify_customer(order-eval-multiseller, "Your order shipped partially. Item(s) collectible1 are awaiting restock.")
```
</details>
