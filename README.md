# jev-wf

Generic order-movement workflow built on **Jev** (TypeSafe's "System One" model, accessed via OpenRouter).

## Goal

Automate an e-commerce order from start to finish, handling heterogeneous items within the same order: an item that needs refrigerated/express delivery (e.g. ice cream), an item that requires a medical prescription (e.g. a controlled drug), a fully digital item (e.g. an e-book, no physical fulfillment step at all), and items without enough stock (partial shipment) — across multiple sellers, each with their own payment approval and their own sourcing network.

The Jev is only ever asked atomic questions about each item's free-text name/description — it never decides the next action on its own, and it never generates free text. It is not an agent: it's a structured-decision model (`Noul`/`Choice`/`Score`) that returns a typed answer with probability and confidence.

The system is split into phases by *what kind of decision* they make — never mix a judgment call with a numeric optimization:

1. **Classification** — the only phase that talks to the Jev. It resolves each item's attributes (blocking requirement type, is digital, shipping SLA, classification confidence) from its free-text description. This is genuine ambiguity resolution over unstructured text — the Jev's job.
2. **Sourcing** — purely deterministic and numeric. Given a list of candidate origins for an item (a nearby store, a regional DC, a customs dock, a made-to-order factory...) with objective data (stock, nominal lead time, current queue, capacity), it picks the one with the lowest *effective* (demand-adjusted) lead time. No ambiguity, no model call — it's a filter and a sort. Two items can come from the very same seller/warehouse and still need separate shipments if their resolved SLA differs (e.g. a refrigerated-express item vs. a standard one) — and conversely, a store that looks fastest on paper can lose to a farther DC once its current backlog is taken into account.
3. **Execution** — reads the classification + sourcing results and triggers the workflow's actions. Has no knowledge that a model was ever involved anywhere upstream. It is **event-driven**, not one synchronous pass: payment clearing, a warehouse restocking, a carrier scan, damage during packing, and a return request each arrive as their own event (see "How the workflow moves" below).

There is no structured product catalog/ERP yet (greenfield project), which is why classification currently comes from the Jev instead of from stored data. Sourcing candidates are also fake/in-memory for the same reason — in a real system they'd come from an actual inventory/logistics service, not the Jev.

## How the workflow moves

The order has no state machine of its own — `OrderStatusAggregator` always derives it from the items (`Models/OrderStatusAggregator.cs`): `Cancelled` if every item is cancelled, `Invoiced`/`PartiallyInvoiced` once every item is terminal, or otherwise the *least-advanced* item's stage - the bottleneck holding the rest of the order back.

Each **item** (`Models/ItemStatus.cs`) moves through a broader lifecycle than a single synchronous pass can express, because a marketplace order can have one seller's items proceed while another's are still clearing payment, and because several stages only really happen when something *external* says so:

```
Pending
  → AwaitingSellerConfirmation → AwaitingPaymentApproval   [gated per seller]
      → AwaitingBlockingRequirementValidation (if any)     [prescription, age verification, ...]
      → AwaitingManualReview (if classification confidence is low)
      → Reserved  --(no stocked origin)-->  Deferred  --(RestockEvent)-->  re-run sourcing
      → AwaitingCancellationWindow → ReadyForHandling → Handling  --(HandlingExceptionEvent)-->  Deferred
      → VerifyingInvoice → Invoiced → Shipped → Delivered
          → ReturnRequested → Returned → Refunded
  → CancellationRequested → Cancelling → Cancelled   [reachable from anywhere above]
```

`OrderWorkflowRunner` (`Workflow/OrderWorkflowRunner.cs`) exposes this as three entry points instead of one big method:

- **`Start(order)`** — every item moves up to `AwaitingPaymentApproval`. Nothing past that point runs yet.
- **`HandleEvent(order, event)`** — reacts to one `OrderEvent` (`Workflow/OrderEvent.cs`): `PaymentApprovedEvent`/`PaymentDeniedEvent` (per seller), `RestockEvent` (re-runs sourcing for one item), `HandlingExceptionEvent` (sends an item back to `Deferred`), `CarrierDeliveredEvent`, `ReturnRequestedEvent`. A digital item completes instantly once its seller's payment clears; a physical item runs the classification-driven gates (blocking requirement, confidence escalation), then sourcing.
- **`Finalize(order)`** — the one thing that only makes sense to evaluate across the *whole* order: groups every item currently awaiting invoicing into shipments by `(origin, shipping method)` — never by seller — notifies the customer, and closes the order once every item is terminal. Safe to call as many times as needed; it only ever touches items currently ready to invoice.

The real API in use is OpenRouter's **Decisions API**: `POST https://openrouter.ai/api/alpha/decisions`, model `typesafe/jev-1.13`. It is not the chat-completions format — see `JevWf.Decisions/DecisionsClient.cs`.

## Structure

Three projects, split by responsibility — the dependency graph flows one way, `Orders → Classification → Decisions`:

- **`src/JevWf.Decisions`** — generic, domain-agnostic client for the Decisions API (`DecisionQuestion`, `DecisionsClient`). Doesn't know what an "order" is; reusable for any future workflow that needs to ask the Jev something.
- **`src/JevWf.Classification`** — the order-specific classification phase, referencing `JevWf.Decisions`. `OrderItemClassifier` asks the Jev and returns `ItemAttributes` per item (blocking requirement type, digital flag, shipping method, weakest-link confidence). This is the *only* project that talks to the Jev.
- **`src/JevWf.Orders`** — the deterministic phases (sourcing + execution), referencing `JevWf.Classification` only to wire things together in `Program.cs`. Neither the sourcing resolver nor the workflow engine ever calls it:
  - `Models` — `Order`, `OrderItem` (seller, resolved attributes, chosen origin), `ItemStatus`, `OrderStatusAggregator`
  - `Tools` — catalog of actions the workflow can trigger (not model tools — these are the code's own actions)
  - `Sourcing` — `SourcingCandidate`, `SourcingResolver` (the numeric origin-picking logic) and `FakeInventoryCatalog` (in-memory stand-in for a real inventory/logistics system; supports `Restock` for `RestockEvent`)
  - `Workflow` — `OrderEvent` (the external triggers), `OrderWorkflowRunner` (`Start`/`HandleEvent`/`Finalize` — no async, no network) and `FakeOrderBackend` (in-memory action log, to run without a real ERP)
  - `Program.cs` — sample entry point: classify, then walk a 5-item order through payment, a restock, and carrier delivery

## Running

```
$env:OPENROUTER_API_KEY = "sk-or-v1-..."
dotnet run --project src/JevWf.Orders
```

The sample order has 5 items across 5 sellers: ice cream, controlled medication, an e-book, headphones, and a collectible that starts out of stock everywhere. It walks through payment approval for every seller, a restock event that reopens the deferred item, and carrier-delivery events for every physical item - printing the item states after payment clears, the full execution log, and the order's aggregated status at the end.

## Scenario tests (the solution, not the Jev)

`src/JevWf.Evaluation` runs the **full pipeline** (Classification → Sourcing → the event-driven Workflow) end to end against a set of predefined order scenarios, and asserts the outcome is what it should be. This is not about whether the Jev is stable in isolation - it's about whether *the solution that uses it* decides and executes the right workflow for a given order: which tools get called, how many shipments come out, which origin each one ships from, which items get deferred/cancelled/returned.

Each scenario (`src/JevWf.Evaluation/Scenarios/ScenarioCatalog.cs`) bundles an order (with sellers), the sourcing candidates each item could ship from, an ordered list of `OrderEvent`s to replay after `Start` (including explicit `FinalizeEvent`s, since the scenario itself controls exactly when shipment-grouping happens relative to other events), and the expected outcome per item plus the expected shipment groups. The runner classifies with a real call to the Decisions API, runs sourcing + the workflow exactly like production code, and compares the actual outcome against the expected one - reporting `PASS`/`FAIL` per scenario (colored in the console) with the full actual state and execution log printed either way, plus a diff on mismatch.

```
dotnet run --project src/JevWf.Evaluation            # all scenarios
dotnet run --project src/JevWf.Evaluation -- simple-shirt multi-seller-complex   # a subset
```

Current scenarios:
- **`simple-shirt`** - one item, one seller, standard shipping, all the way to `Delivered`. Trivial baseline.
- **`multi-seller-complex`** - a fridge and an ice cream from two different sellers, shipping from different origins with different SLAs (standard vs. refrigerated express), plus a third item whose only seller has zero stock everywhere. Asserts they never get merged into one shipment just because it's the same order, and that the out-of-stock item stays `Deferred`.
- **`mixed-attributes`** - the 5-item order from "Running" above (prescription + digital + cold-chain + the queue/effective-lead-time sourcing effect + a deferred item), kept as a regression scenario so that already-validated behavior stays under assertion.
- **`same-origin-merges-across-sellers`** - the flip side of `multi-seller-complex`: two items from different sellers that both route through the same origin+SLA. Asserts they *do* merge into a single shipment - grouping is decided by origin+SLA, never by seller.
- **`prescription-denied`** - the generic blocking-requirement gate comes back denied (`FakeOrderBackend` takes a configurable set of item IDs to deny). Asserts the item is cancelled before sourcing ever runs, while an unrelated item from a different seller in the same order still ships.
- **`heterogeneous-network`** - five items, five origin *types* (express hub, customs dock, regional warehouse, made-to-order factory, cold DC) and all three SLAs (`standard`/`express`/`refrigerated_express`) at once, plus a sourcing decision made on pure lead time (dock vs. warehouse) with no queue effect involved.
- **`payment-denied`** - a seller's payment never clears. The item is cancelled straight from `AwaitingPaymentApproval`, before any classification gate or sourcing runs.
- **`restock-reopens-deferred-item`** - out of stock everywhere at first (`Deferred`), then a `RestockEvent` lets sourcing find an origin and the item ships and gets delivered.
- **`handling-exception-reroutes-to-resourcing`** - the item is damaged during packing at its first-choice origin (`HandlingExceptionEvent`); re-sourcing reroutes it to the next-best origin instead of retrying the same one.
- **`return-after-delivery`** - a delivered item gets a return request (`ReturnRequestedEvent`) and moves to `Returned` with a refund logged - the one post-delivery path no other scenario reaches.

Not covered by a scenario: escalation to `AwaitingManualReview` on low classification confidence. Confidence comes from the real Jev call, and the baseline consistency run showed it's reliably high (0.93-1.00) for unambiguous descriptions - forcing a low-confidence case would mean writing a deliberately ambiguous description, which trades a deterministic scenario for a flaky one. The escalation path exists and is exercised implicitly (every scenario's confidence is checked against the threshold), just never on the "rejected" branch.

## Real test output (2026-09-24)

`mixed-attributes`, executed against the real Decisions API:

```
Execution log:
  - request_seller_confirmation(item1, seller=seller-frozen-treats) -> confirmed
  - await_payment_approval(item1, seller=seller-frozen-treats)
  ... (same for item2..item5, each its own seller)
  - set_shipping_method(item1, refrigerated_express)
  - reserve_item_stock(item1, origin=cold-dc-sp, qty=1)
  - start_fulfillment(item1)
  - request_blocking_requirement_validation(item2, type=prescription, attachment-item2) -> approved
  - reserve_item_stock(item2, origin=central-pharmacy-sp, qty=1)
  - mark_item_digital_only(item3)
  - complete_item(item3)
  - reserve_item_stock(item4, origin=regional-dc-sp, qty=1)
  - defer_item(item5, "no sourcing origin with available stock")
  - create_shipment(order-eval-mixed, origin=cold-dc-sp, method=refrigerated_express, items=[item1])
  - create_shipment(order-eval-mixed, origin=central-pharmacy-sp, method=standard, items=[item2])
  - create_shipment(order-eval-mixed, origin=regional-dc-sp, method=standard, items=[item4])
  - notify_customer(order-eval-mixed, "Your order shipped partially. Item(s) item5 are awaiting restock.")
  - mark_item_delivered(item1) / (item2) / (item4)

Items: item1 Delivered@cold-dc-sp | item2 (prescription) Delivered@central-pharmacy-sp |
       item3 (digital) Delivered | item4 Delivered@regional-dc-sp | item5 Deferred
[PASS] mixed-attributes
```

`handling-exception-reroutes-to-resourcing`, showing recovery via events:

```
Execution log:
  - reserve_item_stock(vase1, origin=fragile-origin-a-sp, qty=1)
  - start_fulfillment(vase1)
  - handle_handling_exception(vase1, "damaged during packing")
  - defer_item(vase1, "handling exception: damaged during packing")
  - reserve_item_stock(vase1, origin=fragile-origin-b-rj, qty=1)
  - create_shipment(order-eval-handling-exception, origin=fragile-origin-b-rj, method=standard, items=[vase1])
  - mark_item_delivered(vase1)
  - close_order(order-eval-handling-exception)

Items: vase1 Delivered@fragile-origin-b-rj
[PASS] handling-exception-reroutes-to-resourcing
```

All 10 scenarios: `== Summary: 10/10 scenarios passed ==`

## Status

Functional prototype, validated end-to-end against the real API via the scenario tests above, covering the full item lifecycle (payment gating per seller, generic blocking requirements, sourcing with restock recovery, handling exceptions, delivery, and returns). Still missing: real confidence-threshold tuning (today `AwaitingManualReview` triggers at a fixed 0.75 cutoff, untested against real low-confidence cases - see above), a real event source (today's `RestockEvent`/`CarrierDeliveredEvent`/etc. are only ever fired by test/sample code, not by an actual warehouse system or carrier webhook), partial-quantity fulfillment (every scenario ships a full single unit; splitting one item's quantity across origins isn't modeled), and replacing the fake backend/inventory with real integrations.
