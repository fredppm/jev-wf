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
| `when` | `rule` (code, e.g. `order.total > 100 && item.quantity >= 2`) and/or `jev` (a statement the Jev judges). Fields: `order.total`, `order.sellerId`, `order.itemCount`, `item.unitPrice`, `item.quantity`, `item.total` |
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
failure) → `issue_invoice`. Broken catalogs fail at load time.

## The Jev and the default workflow

Per item (and per order, for order-level statements) the builder makes at most two Jev calls: one
with a yes/no question per `jev` condition, one with a choice question per point where pieces
compete. Choice answers carry their own confidence; a yes/no answer only carries the probability
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
| `simple-shirt` | Baseline: one item, standard shipping |
| `prescription` | Prescription required for one item of the order, not for the other |
| `digital-ebook` | Digital delivery instead of stock and shipping |
| `high-value-fraud-check` | Order above R$ 100: fraud check and score before payment |
| `free-sample` | Order costing R$ 0: no payment |
| `multi-seller` | Three sellers, three isolated workflows, standard / cold chain / express shipping |
| `two-sellers-isolated` | Two sellers never share a workflow |
| `custom-pharmacy-express` | Seller chain replacing handling, plus prescription and express shipping |

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
