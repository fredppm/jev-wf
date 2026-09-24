# jev-wf

Generic order-movement workflow built on **Jev** (TypeSafe's "System One" model, accessed via OpenRouter).

## Goal

Automate an e-commerce order from start to finish, handling heterogeneous items within the same order: an item that needs refrigerated/express delivery (e.g. ice cream), an item that requires a medical prescription (e.g. a controlled drug), a fully digital item (e.g. an e-book, no physical fulfillment step at all), and items without enough stock (partial shipment).

The code owns the flow — it decides the sequence of actions, calls the backend, and decides when to close the order. The Jev is only asked atomic questions about each item — it never decides the next action on its own, and it never generates free text. It is not an agent: it's a structured-decision model (`Noul`/`Choice`/`Score`) that returns a typed answer with probability and confidence, and the workflow deterministically combines those answers.

There is no structured product catalog/ERP yet (greenfield project): the Jev infers each item's attributes — requires prescription, is digital, shipping urgency — from the product's free-text name and description.

## How it works

1. `OrderWorkflowRunner` builds the `state` for the whole order (all items together) and makes **a single call** to the Decisions API, asking for each item: does it require a prescription? is it digital? what shipping urgency does it need?
2. The Jev answers each question with a calibrated probability/choice.
3. The code applies a decision threshold (0.5) to the answers and triggers the matching actions from the catalog (`Tools/OrderToolCatalog.cs`): validate prescription, mark as digital, set shipping method, reserve stock, etc.
4. Items without enough stock are deferred (`defer_item`); the rest are grouped into a partial shipment (`create_partial_shipment`).
5. The order closes once every item reaches a terminal state (delivered or cancelled).

The real API in use is OpenRouter's **Decisions API**: `POST https://openrouter.ai/api/alpha/decisions`, model `typesafe/jev-1.13`. It is not the chat-completions format — see `JevWf.Decisions/DecisionsClient.cs`.

## Structure

Two projects, split by responsibility:

- **`src/JevWf.Decisions`** — generic, domain-agnostic client for the Decisions API (`DecisionQuestion`, `DecisionsClient`). Reusable for any future workflow that needs to ask the Jev something, not just orders.
- **`src/JevWf.Orders`** — the order domain, referencing `JevWf.Decisions`:
  - `Models` — `Order`, `OrderItem`, `ItemStatus`
  - `Tools` — catalog of actions the workflow can trigger (not model tools — these are the code's own actions)
  - `Workflow` — `OrderWorkflowRunner` (the decision engine) and `FakeOrderBackend` (in-memory stock/state, to run without a real ERP)
  - `Program.cs` — sample entry point

## Running

```
$env:OPENROUTER_API_KEY = "sk-or-v1-..."
dotnet run --project src/JevWf.Orders
```

## Real test output (2026-09-24)

Order with 4 items: ice cream, controlled medication, e-book, headphones (deliberately out of stock). The Jev decided every case correctly in a single call, with no hardcoded rule like "ice cream = express":

```
== Execution log ==
- set_shipping_method(item1, refrigerated_express)
- check_item_stock(item1) -> 5 available
- reserve_item_stock(item1, 1)
- start_fulfillment(item1)
- request_prescription_validation(item2, attachment-item2) -> approved
- set_shipping_method(item2, standard)
- check_item_stock(item2) -> 5 available
- reserve_item_stock(item2, 1)
- start_fulfillment(item2)
- mark_item_digital_only(item3)
- complete_item(item3)
- set_shipping_method(item4, standard)
- check_item_stock(item4) -> 0 available
- defer_item(item4, "insufficient stock")
- create_partial_shipment(order-001, [item1, item2])
- complete_item(item1)
- complete_item(item2)
- notify_customer(order-001, "Your order shipped partially. Item(s) item4 are awaiting restock.")

== Final item status ==
- item1 (Pistachio ice cream 1L): Delivered
- item2 (Amoxicillin 500mg): Delivered
- item3 (E-book: Introduction to AI Workflows): Delivered
- item4 (Bluetooth headphones): Deferred
```

- **item1 (ice cream)** → the Jev recognized, from the description alone, that it was perishable and needed cold chain + fast delivery.
- **item2 (medication)** → the Jev identified the prescription requirement before releasing fulfillment.
- **item3 (e-book)** → the Jev identified it as fully digital and skipped all physical logistics.
- **item4 (out of stock)** → a deterministic decision made by the code (not the Jev): the item was deferred, the rest shipped as a partial shipment.

## Status

Functional prototype, validated end-to-end against the real API. Still missing: reopening the workflow for deferred items once stock is replenished, defining confidence thresholds for escalation (today it's a fixed 0.5 cutoff), and replacing `FakeOrderBackend` with a real integration.
