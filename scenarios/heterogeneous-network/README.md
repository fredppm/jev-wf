# heterogeneous-network

> Five items, five origin types (express hub, customs dock, regional warehouse, made-to-order factory, cold DC), all three SLAs at once.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `part1` | seller-bike-parts | Urgent replacement bicycle part, small physical item, needs fast delivery but no refrigeration, no legal restriction. | 1 |
| `import1` | seller-imports | Imported physical product, ordinary shipping, no special urgency, no legal restriction. | 1 |
| `factory1` | seller-custom-factory | Made-to-order physical product, ordinary shipping, no delivery urgency, no legal restriction. | 1 |
| `fish1` | seller-seafood | Perishable seafood, must stay refrigerated and requires cold-chain fast delivery, no legal restriction. | 1 |
| `office1` | seller-office-supplies | Ordinary office supply, physical product, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `part1` | seller-parts-express-hub-poa | 6 | 2 |
| `import1` | seller-electronics-dock-santos | 30 | 72 |
| `import1` | seller-electronics-wh-curitiba | 12 | 20 |
| `factory1` | seller-custom-factory-joinville | 50 | 96 |
| `fish1` | seller-seafood-cold-dc-santos | 10 | 3 |
| `office1` | seller-office-wh-joinville | 100 | 10 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-bike-parts
2. `PaymentApprovedEvent` sellerId=seller-imports
3. `PaymentApprovedEvent` sellerId=seller-custom-factory
4. `PaymentApprovedEvent` sellerId=seller-seafood
5. `PaymentApprovedEvent` sellerId=seller-office-supplies
6. `FinalizeEvent`
7. `CarrierDeliveredEvent` itemId=part1
8. `CarrierDeliveredEvent` itemId=import1
9. `CarrierDeliveredEvent` itemId=factory1
10. `CarrierDeliveredEvent` itemId=fish1
11. `CarrierDeliveredEvent` itemId=office1
12. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `part1` | none | - | express | seller-parts-express-hub-poa | **Delivered** |
| `import1` | none | - | standard | seller-electronics-wh-curitiba | **Delivered** |
| `factory1` | none | - | standard | seller-custom-factory-joinville | **Delivered** |
| `fish1` | none | - | refrigerated_express | seller-seafood-cold-dc-santos | **Delivered** |
| `office1` | none | - | standard | seller-office-wh-joinville | **Delivered** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| seller-parts-express-hub-poa | express | `part1` |
| seller-electronics-wh-curitiba | standard | `import1` |
| seller-custom-factory-joinville | standard | `factory1` |
| seller-seafood-cold-dc-santos | refrigerated_express | `fish1` |
| seller-office-wh-joinville | standard | `office1` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(part1, seller=seller-bike-parts) -> confirmed
await_payment_approval(part1, seller=seller-bike-parts)
request_seller_confirmation(import1, seller=seller-imports) -> confirmed
await_payment_approval(import1, seller=seller-imports)
request_seller_confirmation(factory1, seller=seller-custom-factory) -> confirmed
await_payment_approval(factory1, seller=seller-custom-factory)
request_seller_confirmation(fish1, seller=seller-seafood) -> confirmed
await_payment_approval(fish1, seller=seller-seafood)
request_seller_confirmation(office1, seller=seller-office-supplies) -> confirmed
await_payment_approval(office1, seller=seller-office-supplies)
[jev] part1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.05)
[jev] part1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(part1, express)
reserve_item_stock(part1, origin=seller-parts-express-hub-poa, qty=1)
start_fulfillment(part1)
[jev] import1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.07)
[jev] import1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(import1, standard)
reserve_item_stock(import1, origin=seller-electronics-wh-curitiba, qty=1)
start_fulfillment(import1)
[jev] factory1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.07)
[jev] factory1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(factory1, standard)
reserve_item_stock(factory1, origin=seller-custom-factory-joinville, qty=1)
start_fulfillment(factory1)
[jev] fish1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.07)
[jev] fish1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(fish1, refrigerated_express)
reserve_item_stock(fish1, origin=seller-seafood-cold-dc-santos, qty=1)
start_fulfillment(fish1)
[jev] office1: checks escalate_for_manual_review=no (0.03), fraud_check=no (0.05)
[jev] office1: options=[cancel_item, defer_item, mark_item_digital_only, reserve_item_stock] -> reserve_item_stock (confidence 1.00)
set_shipping_method(office1, standard)
reserve_item_stock(office1, origin=seller-office-wh-joinville, qty=1)
start_fulfillment(office1)
create_shipment(order-eval-heterogeneous-network, origin=seller-parts-express-hub-poa, method=express, items=[part1])
mark_item_shipped(part1)
create_shipment(order-eval-heterogeneous-network, origin=seller-electronics-wh-curitiba, method=standard, items=[import1])
mark_item_shipped(import1)
create_shipment(order-eval-heterogeneous-network, origin=seller-custom-factory-joinville, method=standard, items=[factory1])
mark_item_shipped(factory1)
create_shipment(order-eval-heterogeneous-network, origin=seller-seafood-cold-dc-santos, method=refrigerated_express, items=[fish1])
mark_item_shipped(fish1)
create_shipment(order-eval-heterogeneous-network, origin=seller-office-wh-joinville, method=standard, items=[office1])
mark_item_shipped(office1)
notify_customer(order-eval-heterogeneous-network, "Your order has been confirmed and is on its way.")
mark_item_delivered(part1)
mark_item_delivered(import1)
mark_item_delivered(factory1)
mark_item_delivered(fish1)
mark_item_delivered(office1)
close_order(order-eval-heterogeneous-network)
```
</details>
