# mixed-attributes

> Regression case: prescription + digital + cold-chain + queue-effect sourcing + a deferred item, all in one order.

**Result:** ✅ PASS · [input.json](input.json) · [output.json](output.json) · [← all scenarios](../../README.md#scenario-tests)

## Input

What the pipeline receives: the order, the stock available per origin, and the events fired after `Start`.

**Order items**

| Item | Seller | Description (sent to the Jev) | Qty |
|---|---|---|---|
| `item1` | seller-frozen-treats | Artisanal pistachio ice cream tub, must be kept frozen until delivery, no legal restriction. | 1 |
| `item2` | seller-pharmacy | Controlled antibiotic, requires a medical prescription to be presented. | 1 |
| `item3` | seller-digital-books | Digital PDF book, delivered by download/email after purchase, no legal restriction. | 1 |
| `item4` | seller-electronics | Wireless headphones, ordinary physical product, no delivery urgency, no legal restriction. | 1 |
| `item5` | seller-collectibles | Collectible sneakers, ordinary physical product, no delivery urgency, no legal restriction. | 1 |

**Sourcing candidates** (lowest effective lead time with stock wins)

| Item | Origin | Stock | Effective lead time (h) |
|---|---|---|---|
| `item1` | cold-dc-sp | 5 | 4 |
| `item2` | central-pharmacy-sp | 5 | 2 |
| `item4` | corner-store-sp | 3 | 41 |
| `item4` | regional-dc-sp | 40 | 24.1 |
| `item5` | flagship-store-sp | 0 | 1 |

**Events** (in order)

1. `PaymentApprovedEvent` sellerId=seller-frozen-treats
2. `PaymentApprovedEvent` sellerId=seller-pharmacy
3. `PaymentApprovedEvent` sellerId=seller-digital-books
4. `PaymentApprovedEvent` sellerId=seller-electronics
5. `PaymentApprovedEvent` sellerId=seller-collectibles
6. `FinalizeEvent`
7. `CarrierDeliveredEvent` itemId=item1
8. `CarrierDeliveredEvent` itemId=item2
9. `CarrierDeliveredEvent` itemId=item4
10. `FinalizeEvent`

## Output

Final state after all events: what the Jev classified, where each item was sourced, and how shipments were grouped.

| Item | Blocking req. | Digital | SLA | Origin | Final status |
|---|---|---|---|---|---|
| `item1` | none | - | refrigerated_express | cold-dc-sp | **Delivered** |
| `item2` | prescription | - | standard | central-pharmacy-sp | **Delivered** |
| `item3` | none | yes | standard | - | **Delivered** |
| `item4` | none | - | standard | regional-dc-sp | **Delivered** |
| `item5` | none | - | standard | - | **Deferred** |

**Shipments** (grouped by origin + SLA)

| Origin | SLA | Items |
|---|---|---|
| cold-dc-sp | refrigerated_express | `item1` |
| central-pharmacy-sp | standard | `item2` |
| regional-dc-sp | standard | `item4` |

<details><summary>Execution log (tool calls)</summary>

```
request_seller_confirmation(item1, seller=seller-frozen-treats) -> confirmed
await_payment_approval(item1, seller=seller-frozen-treats)
request_seller_confirmation(item2, seller=seller-pharmacy) -> confirmed
await_payment_approval(item2, seller=seller-pharmacy)
request_seller_confirmation(item3, seller=seller-digital-books) -> confirmed
await_payment_approval(item3, seller=seller-digital-books)
request_seller_confirmation(item4, seller=seller-electronics) -> confirmed
await_payment_approval(item4, seller=seller-electronics)
request_seller_confirmation(item5, seller=seller-collectibles) -> confirmed
await_payment_approval(item5, seller=seller-collectibles)
set_shipping_method(item1, refrigerated_express)
reserve_item_stock(item1, origin=cold-dc-sp, qty=1)
start_fulfillment(item1)
request_blocking_requirement_validation(item2, type=prescription, attachment-item2) -> approved
set_shipping_method(item2, standard)
reserve_item_stock(item2, origin=central-pharmacy-sp, qty=1)
start_fulfillment(item2)
mark_item_digital_only(item3)
complete_item(item3)
set_shipping_method(item4, standard)
reserve_item_stock(item4, origin=regional-dc-sp, qty=1)
start_fulfillment(item4)
set_shipping_method(item5, standard)
defer_item(item5, "no sourcing origin with available stock")
create_shipment(order-eval-mixed, origin=cold-dc-sp, method=refrigerated_express, items=[item1])
mark_item_shipped(item1)
create_shipment(order-eval-mixed, origin=central-pharmacy-sp, method=standard, items=[item2])
mark_item_shipped(item2)
create_shipment(order-eval-mixed, origin=regional-dc-sp, method=standard, items=[item4])
mark_item_shipped(item4)
notify_customer(order-eval-mixed, "Your order shipped partially. Item(s) item5 are awaiting restock.")
mark_item_delivered(item1)
mark_item_delivered(item2)
mark_item_delivered(item4)
notify_customer(order-eval-mixed, "Your order shipped partially. Item(s) item5 are awaiting restock.")
```
</details>
