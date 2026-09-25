# jev-wf

Builds the workflow each order must run. The input is an order group; the output is one
deterministic workflow per order (per seller). The **Jev** (`typesafe/jev-1.13` via OpenRouter's
Decisions API) is used only while building it, to judge things code cannot (does this item need a
prescription? is it digital? does it need cold chain?). Nothing here runs a workflow.

```
orderGroup.json ──► rules (code) + Jev ──► connect pieces by type ──► validate ──► workflows.json
                                                                        │ invalid, or Jev not confident
                                                                        └──► default workflow
```

## Input and output

**Input: the order group.** One `order` per seller; each order is isolated (its own workflow,
stock and origins).

```json
{
  "orderGroupId": "og-1",
  "orders": [
    { "orderId": "og-1-01", "sellerId": "seller-pharmacy",
      "items": [ { "itemId": "i1", "productName": "Amoxicillin 500mg",
                   "description": "Controlled antibiotic, needs a medical prescription.",
                   "quantity": 1, "unitPrice": 35.00 } ] }
  ]
}
```

**Output: the workflows.** Only pieces (nodes) and how they connect (edges). An edge without
`to` ends the flow at a terminal type. `mode` is `jev`, or `default` when the fallback was used.

```json
{ "orderGroupId": "og-1",
  "workflows": [ { "orderId": "og-1-01", "mode": "jev", "start": "confirm_seller",
                   "nodes": [ { "id": "i1/request_prescription", "piece": "request_prescription", "scope": "item", "itemId": "i1", "publicStatus": "waiting_prescription" } ],
                   "edges": [ { "from": "i1/request_prescription", "port": "approved", "type": "item_released", "to": "i1/reserve_stock" },
                              { "from": "i1/request_prescription", "port": "denied", "type": "item_cancelled" } ] } ] }
```

## Pieces

A piece is a puzzle piece: it accepts types (`in`) and leaves through typed ports (`out`). Two
pieces connect only when one produces a type the other accepts.

```json
{
  "name": "payment",
  "kind": "fixed",
  "scope": "order",
  "when": { "rule": "order.total > 0" },
  "in": "order_confirmed",
  "out": { "approved": "order_confirmed", "denied": "order_cancelled" },
  "onException": "manual_review_required",
  "publicStatus": "payment_pending",
  "config": { "timeoutMinutes": 30, "retries": 2 }
}
```

| Field | Meaning |
|---|---|
| `kind` | `fixed`: always there. `required`: must be there whenever its condition holds. `optional`: enters when its condition holds, or competes with other pieces for the same type |
| `scope` | `order` or `item` |
| `when` | `rule` (code, e.g. `order.total > 100 && item.quantity >= 2`) and/or `jev` (a statement the Jev judges must hold) or `jevNot` (must not hold; two pieces with the same statement, one `jev` and one `jevNot`, are alternatives decided by one question). Fields: `order.total`, `order.sellerId`, `order.itemCount`, `item.unitPrice`, `item.quantity`, `item.total` |
| `in` / `out` | The connection types. Types are free strings; custom pieces can create new ones |
| `onException` | Type produced when the piece fails. Default: `manual_review_required` (a person decides) |
| `publicStatus` | What everyone outside sees while the flow is in this piece |
| `default` | Picked when several pieces compete and the Jev is not deciding |
| `config` | The piece's own parameters, copied into the workflow |

### How pieces are connected

- **Gates.** A piece that gives back the type it accepts (`payment`: `order_confirmed` →
  `order_confirmed`) is plugged in at that type; so is a chain that does
  (`fraud_check` → `evaluate_fraud_score` → `order_confirmed`). Active gates run in catalog order,
  and a gate whose condition does not hold is simply not there.
- **Advancing pieces.** After the gates, one piece moves the flow forward. If several compete
  (`reserve_stock` vs `deliver_digital`, `ship_standard` vs `ship_express` vs `ship_cold_chain`),
  the Jev picks one.
- **Order → items.** An order type that no order piece accepts splits the flow into one branch per
  item (`release_items` → `item_released`).
- **Loops.** A type can lead back to a piece already placed: a handling or delivery failure
  (`resourcing_required`) or a restock (`restock_received`) goes back to `reserve_stock`.
- **Terminals** (`vtex.json` → `types.terminals`) end a flow with a public status. The order's
  status is aggregated from its items by fixed rules; it is not a piece.

## Catalogs

- `catalog/vtex.json`: the VTEX pieces and the fixed types (start, terminals, default exception).
- `catalog/sellers/<sellerId>.json`: custom pieces for that seller's orders.

Custom pieces can **add** pieces (with a VTEX `publicStatus`) or **replace** any VTEX piece with a
chain (`"replaces": "start_handling"`). Seen from outside, the chain must be the same piece: it
accepts the same types, leaves through the same types and fails through the same exception. Inside,
types are free, exceptions can be handled internally, unhandled ones leave through the replaced
piece's exception, and the public status stays the replaced piece's. Example:
`seller-pharmacy-express` replaces `start_handling` with `pick_from_shelf` → `pack` (`repack` on
failure); the VTEX `issue_invoice` still runs after it. `seller-gift-shop` adds `gift_wrap` at
`stock_reserved`. Broken catalogs fail at load time.

What the VTEX catalog decides with the Jev today:

| Point | Pieces |
|---|---|
| Order confirmed (order) | `review_unusual_order` (statement) |
| Item released (gates) | `request_prescription`, `pharmacist_review` (rule + statement), `verify_age` |
| Item released (choice) | `reserve_stock`, `produce_to_order`, `deliver_digital` |
| Ready to ship (gates) | `issue_invoice` (fixed), `require_special_transport` (statement) |
| Ready to ship (choice) | `ship_standard`, `ship_express`, `ship_cold_chain`, `ship_heavy_freight` |
| Item delivered (statement) | `return_window` if it can be taken back, `close_without_return` if not |

## The Jev and the default workflow

Per item (and per order, for order-level statements) the builder makes at most two Jev calls: one
with a yes/no question per `jev` condition, one with a choice question per point where pieces
compete. Statements and descriptions are written about what the piece does ("a prescription must be
collected", "ordinary carriers refuse to take this item"), not as a product category, so the Jev
judges whether that action is needed for that item. Pieces that share a statement share one
question. Choice answers carry their own confidence; a yes/no answer only carries the probability
that the statement holds, so its confidence is how far that is from a coin flip (0.95 and 0.05 are
both 0.95). If any answer is below the confidence threshold (0.7), or the resulting workflow is
invalid (a type nothing accepts, a required piece left out, a node that can never finish), the
order gets the **default workflow**: every `jev` condition counts as true and competing pieces
resolve to the one flagged `default`.

## Scenarios

Each folder in `scenarios/` has `input.json` (order group), `expected.json` (workflows) and
`jev-answers.json` (the decisions the Jev is expected to make, so the build can be tested without
the model).

| Scenario | What it shows |
|---|---|
| `simple-shirt` | Baseline: one item, standard shipping, returnable |
| `prescription` | Prescription for the medicine (not returnable), none for the bottle sold by the same pharmacy |
| `digital-ebook` | Digital delivery instead of stock and shipping |
| `high-value-fraud-check` | Order above R$ 100: fraud check and score before payment |
| `free-sample` | Order costing R$ 0: no payment |
| `multi-seller` | Three isolated workflows: heavy freight, cold chain (not returnable), express |
| `two-sellers-isolated` | Two sellers never share a workflow |
| `custom-pharmacy-express` | Seller chain replacing handling, then the VTEX invoice, prescription, express |
| `age-restricted-whisky` | Age verification before stock |
| `special-transport-solvent` | Flammable solvent needs a certified carrier, booked after the invoice |
| `made-to-order-engraving` | Produced instead of reserved; engraved, so not returnable |
| `mixed-single-seller` | One order, four item branches: digital, frozen, OTC medicine, ordinary |
| `bulk-resale-order` | Order-level Jev statement: 40 phones get a manual review, plus fraud check |
| `pharmacist-quantity-review` | Rule + Jev on the same piece: controlled medicine above 2 units |
| `custom-gift-shop` | Seller adds a piece (gift wrap) only for the item sold as a gift |
| `near-miss-otc-medicine` | Medicine without prescription: no prescription step, still not returnable |
| `near-miss-fresh-fish` | Chilled, not frozen, still cold chain |
| `near-miss-printed-gift-card` | Sounds digital, is a physical card: stock and shipping |
| `near-miss-alcohol-free-beer` | Sounds like alcohol, 0.0%: no age verification |

The `near-miss-*` scenarios are the ones where a careless classification goes wrong; they only
prove something in the real-Jev test.

Four scenarios expect the **default workflow**: the real Jev answers right, but consistently below
the 0.7 threshold (measured over repeated runs): whisky and paint thinner returns (~0.6), printed
gift card returns (~0.6) and the review of an order with 3 boxes of a controlled medicine (~0.67).
Their `jev-answers.json` records those confidences.

## Running

```powershell
# Build workflows for an order group (real Jev)
$env:OPENROUTER_API_KEY = "sk-or-v1-..."
dotnet run --project src/JevWf.Cli -- scenarios/prescription/input.json

# Same, with scripted Jev answers (writes a scenario's expected.json)
dotnet run --project src/JevWf.Cli -- scenarios/prescription/input.json --answers scenarios/prescription/jev-answers.json --out scenarios/prescription/expected.json

# Tests (the real-Jev scenario tests are skipped without OPENROUTER_API_KEY)
dotnet test
```

Options: `--catalog <dir>` (default `catalog`), `--threshold <0-1>` (default 0.7).
