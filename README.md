# jev-wf

Generic order-movement workflow built on **Jev** (TypeSafe's "System One" model, accessed via OpenRouter).

## Goal

Automate an e-commerce order from start to finish, handling heterogeneous items within the same order: an item that needs refrigerated/express delivery (e.g. ice cream), an item that requires a medical prescription (e.g. a controlled drug), a fully digital item (e.g. an e-book, no physical fulfillment step at all), and items without enough stock (partial shipment).

The Jev is only ever asked atomic questions about each item's free-text name/description — it never decides the next action on its own, and it never generates free text. It is not an agent: it's a structured-decision model (`Noul`/`Choice`/`Score`) that returns a typed answer with probability and confidence.

The system is split into phases by *what kind of decision* they make — never mix a judgment call with a numeric optimization:

1. **Classification** — the only phase that talks to the Jev. It resolves each item's attributes (requires prescription, is digital, shipping SLA) from its free-text description. This is genuine ambiguity resolution over unstructured text — the Jev's job.
2. **Sourcing** — purely deterministic and numeric. Given a list of candidate origins for an item (a nearby store, a regional DC, a made-to-order factory...) with objective data (stock, nominal lead time, current queue, capacity), it picks the one with the lowest *effective* (demand-adjusted) lead time. No ambiguity, no model call — it's a filter and a sort. Two items can come from the very same seller/warehouse and still need separate shipments if their resolved SLA differs (e.g. a refrigerated-express item vs. a standard one) — and conversely, a store that looks fastest on paper can lose to a farther DC once its current backlog is taken into account.
3. **Execution** — reads the classification + sourcing results and triggers the workflow's actions. Has no knowledge that a model was ever involved anywhere upstream.

There is no structured product catalog/ERP yet (greenfield project), which is why classification currently comes from the Jev instead of from stored data. Sourcing candidates are also fake/in-memory for the same reason — in a real system they'd come from an actual inventory/logistics service, not the Jev.

## How it works

1. `OrderItemClassifier` (in `JevWf.Classification`) builds the `state` for the whole order (all items together) and makes **a single call** to the Decisions API, asking for each item: does it require a prescription? is it digital? what shipping SLA does it need? It returns each item's resolved `ItemAttributes`.
2. Those attributes are written onto each `OrderItem`.
3. `OrderWorkflowRunner` (in `JevWf.Orders`) reads those attributes — synchronously, no network/model calls — and triggers the matching actions from the catalog (`Tools/OrderToolCatalog.cs`): validate prescription, mark as digital, set shipping method.
4. For each physical item, `SourcingResolver` (in `Sourcing/`) picks the best-stocked, lowest-effective-lead-time origin among that item's candidates (`FakeInventoryCatalog`). An item with no stocked candidate anywhere is deferred (`defer_item`).
5. Items that found an origin are grouped by `(origin, shipping SLA)` — never by "the whole order" — and each group becomes its own `create_shipment` call. Same order, same seller even, doesn't matter: different origin or incompatible SLA always means a separate shipment.
6. The order closes once every item reaches a terminal state (delivered or cancelled).

The real API in use is OpenRouter's **Decisions API**: `POST https://openrouter.ai/api/alpha/decisions`, model `typesafe/jev-1.13`. It is not the chat-completions format — see `JevWf.Decisions/DecisionsClient.cs`.

## Structure

Three projects, split by responsibility — the dependency graph flows one way, `Orders → Classification → Decisions`:

- **`src/JevWf.Decisions`** — generic, domain-agnostic client for the Decisions API (`DecisionQuestion`, `DecisionsClient`). Doesn't know what an "order" is; reusable for any future workflow that needs to ask the Jev something.
- **`src/JevWf.Classification`** — the order-specific classification phase, referencing `JevWf.Decisions`. `OrderItemClassifier` asks the Jev and returns `ItemAttributes` per item. This is the *only* project that talks to the Jev.
- **`src/JevWf.Orders`** — the deterministic phases (sourcing + execution), referencing `JevWf.Classification` only to wire things together in `Program.cs`. Neither the sourcing resolver nor the workflow engine ever calls it:
  - `Models` — `Order`, `OrderItem` (carries the resolved attributes and chosen origin once classified/sourced), `ItemStatus`
  - `Tools` — catalog of actions the workflow can trigger (not model tools — these are the code's own actions)
  - `Sourcing` — `SourcingCandidate`, `SourcingResolver` (the numeric origin-picking logic) and `FakeInventoryCatalog` (in-memory stand-in for a real inventory/logistics system)
  - `Workflow` — `OrderWorkflowRunner` (the purely deterministic decision engine — no async, no network) and `FakeOrderBackend` (in-memory action log, to run without a real ERP)
  - `Program.cs` — sample entry point: classify, then source + run

## Running

```
$env:OPENROUTER_API_KEY = "sk-or-v1-..."
dotnet run --project src/JevWf.Orders
```

## Real test output (2026-09-24)

Order with 5 items: ice cream, controlled medication, e-book, headphones, and a collectible item that's out of stock everywhere. The Jev classified every item correctly in a single call; sourcing and shipment grouping ran entirely in code, no model involved:

```
== Execution log ==
- set_shipping_method(item1, refrigerated_express)
- reserve_item_stock(item1, origin=cold-dc-sp, qty=1)
- start_fulfillment(item1)
- request_prescription_validation(item2, attachment-item2) -> approved
- set_shipping_method(item2, standard)
- reserve_item_stock(item2, origin=central-pharmacy-sp, qty=1)
- start_fulfillment(item2)
- mark_item_digital_only(item3)
- complete_item(item3)
- set_shipping_method(item4, standard)
- reserve_item_stock(item4, origin=regional-dc-sp, qty=1)
- start_fulfillment(item4)
- set_shipping_method(item5, standard)
- defer_item(item5, "no sourcing origin with available stock")
- create_shipment(order-001, origin=cold-dc-sp, method=refrigerated_express, items=[item1])
- complete_item(item1)
- create_shipment(order-001, origin=central-pharmacy-sp, method=standard, items=[item2])
- complete_item(item2)
- create_shipment(order-001, origin=regional-dc-sp, method=standard, items=[item4])
- complete_item(item4)
- notify_customer(order-001, "Your order shipped partially. Item(s) item5 are awaiting restock.")

== Final item status ==
- item1 (Pistachio ice cream 1L): Delivered [origin=cold-dc-sp]
- item2 (Amoxicillin 500mg): Delivered [origin=central-pharmacy-sp]
- item3 (E-book: Introduction to AI Workflows): Delivered [origin=-]
- item4 (Bluetooth headphones): Delivered [origin=regional-dc-sp]
- item5 (Limited edition sneakers): Deferred [origin=-]
```

- **item1 (ice cream)** → the Jev recognized, from the description alone, that it was perishable and needed cold chain + fast delivery.
- **item2 (medication)** → the Jev identified the prescription requirement before releasing fulfillment.
- **item3 (e-book)** → the Jev identified it as fully digital and skipped all physical logistics.
- **item4 (headphones)** → sourcing picked `regional-dc-sp` (24h nominal lead time) over the nominally-faster `corner-store-sp` (1h), because the corner store's queue (200 orders at 5/hour = 40h backlog) made its *effective* lead time worse. A demand-capacity effect, resolved purely by arithmetic — no model call.
- **item5 (out of stock everywhere)** → every sourcing candidate had zero stock, so the item was deferred deterministically.
- Each delivered item shipped from a **different origin**, so the order produced **three separate shipments** instead of one — same order, same customer, zero shared logistics, because origin and SLA never matched across items.

## Status

Functional prototype, validated end-to-end against the real API. Still missing: reopening the workflow for deferred items once stock is replenished (event-driven re-sourcing — e.g. a "vase broke during packing" event should re-run sourcing for just that item), defining confidence thresholds for escalation (today it's a fixed 0.5 cutoff), a generic "blocking requirement" question instead of a dedicated one per case (prescription, age verification, export license, ...), and replacing the fake backend/inventory with real integrations.
