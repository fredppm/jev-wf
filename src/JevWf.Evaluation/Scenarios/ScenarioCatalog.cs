using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;
using JevWf.Orders.Workflow;

namespace JevWf.Evaluation.Scenarios;

// The set of scenarios the solution is evaluated against, from trivial to the multi-seller
// cases that motivated this harness, plus one scenario per event type the workflow reacts to
// (payment denial, restock, handling exception, return). Descriptions are written to be
// unambiguous - a scenario exercises whether the *pipeline* gets the outcome right, not whether
// a genuinely ambiguous description could go either way.
public static class ScenarioCatalog
{
    public static IReadOnlyList<ScenarioDefinition> All => new[]
    {
        SimpleShirt(),
        MultiSellerComplex(),
        MixedAttributes(),
        SameOriginMergesAcrossSellers(),
        BlockingRequirementDenied(),
        HeterogeneousNetwork(),
        PaymentDenied(),
        RestockReopensDeferredItem(),
        HandlingExceptionReroutesToResourcing(),
        ReturnAfterDelivery()
    };

    // Baseline case: one item, one seller, nothing special. If this doesn't pass, nothing else matters.
    private static ScenarioDefinition SimpleShirt()
    {
        var order = new Order
        {
            OrderId = "order-eval-simple-shirt",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 100 - São Paulo, SP",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "shirt1",
                    SellerId = "seller-apparel",
                    ProductName = "Plain cotton t-shirt (size M)",
                    Description = "Ordinary cotton t-shirt, no special handling, no legal restriction, not digital.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["shirt1"] = new()
            {
                new SourcingCandidate("seller-apparel-dc-sp", AvailableStock: 10, LeadTimeNominalHours: 6, CurrentQueue: 0, CapacityPerHour: 10)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-apparel"),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("shirt1"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["shirt1"] = new("none", false, "standard", ItemStatus.Delivered, "seller-apparel-dc-sp")
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("seller-apparel-dc-sp", "standard", new[] { "shirt1" })
        };

        return new ScenarioDefinition(
            Name: "simple-shirt",
            Description: "One item, one seller, standard shipping. Trivial baseline, all the way to Delivered.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }

    // A fridge and an ice cream from two different sellers, shipping from different origins with
    // different SLAs, plus a third item whose only seller has zero stock everywhere.
    private static ScenarioDefinition MultiSellerComplex()
    {
        var order = new Order
        {
            OrderId = "order-eval-multiseller",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Av. Exemplo, 500 - Rio de Janeiro, RJ",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "fridge1",
                    SellerId = "seller-appliances",
                    ProductName = "400L double-door refrigerator",
                    Description = "Large major home appliance. Needs careful freight handling but is not perishable, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "icecream1",
                    SellerId = "seller-frozen-foods",
                    ProductName = "Gourmet ice cream tub 2L",
                    Description = "Frozen dessert, perishable, must stay frozen and requires cold-chain fast delivery, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "collectible1",
                    SellerId = "seller-collectibles",
                    ProductName = "Imported hand-painted ceramic vase",
                    Description = "Decorative collectible item, ordinary physical product, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["fridge1"] = new()
            {
                new SourcingCandidate("seller-appliances-wh-rj", AvailableStock: 5, LeadTimeNominalHours: 48, CurrentQueue: 0, CapacityPerHour: 5)
            },
            ["icecream1"] = new()
            {
                new SourcingCandidate("seller-frozen-foods-dc-sp", AvailableStock: 8, LeadTimeNominalHours: 3, CurrentQueue: 0, CapacityPerHour: 10)
            },
            // The one candidate this seller has is out of stock everywhere, and this scenario
            // never restocks it - it stays Deferred (RestockReopensDeferredItem covers recovery).
            ["collectible1"] = new()
            {
                new SourcingCandidate("seller-collectibles-store-sp", AvailableStock: 0, LeadTimeNominalHours: 2, CurrentQueue: 0, CapacityPerHour: 5)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-appliances"),
            new PaymentApprovedEvent("seller-frozen-foods"),
            new PaymentApprovedEvent("seller-collectibles"),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("fridge1"),
            new CarrierDeliveredEvent("icecream1"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["fridge1"] = new("none", false, "standard", ItemStatus.Delivered, "seller-appliances-wh-rj"),
            ["icecream1"] = new("none", false, "refrigerated_express", ItemStatus.Delivered, "seller-frozen-foods-dc-sp"),
            ["collectible1"] = new("none", false, "standard", ItemStatus.Deferred, null)
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("seller-appliances-wh-rj", "standard", new[] { "fridge1" }),
            new("seller-frozen-foods-dc-sp", "refrigerated_express", new[] { "icecream1" })
        };

        return new ScenarioDefinition(
            Name: "multi-seller-complex",
            Description: "Fridge + ice cream from different sellers/origins/SLAs, plus a third item whose seller is out of stock everywhere.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }

    // The 5-item order from the README's "Real test output" - kept as a regression scenario so
    // the already-validated prescription/digital/queue-effect behavior stays under assertion.
    private static ScenarioDefinition MixedAttributes()
    {
        var order = new Order
        {
            OrderId = "order-eval-mixed",
            CustomerEmail = "customer@example.com",
            ShippingAddress = "123 Example St - São Paulo, SP",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "item1",
                    SellerId = "seller-frozen-treats",
                    ProductName = "Pistachio ice cream 1L",
                    Description = "Artisanal pistachio ice cream tub, must be kept frozen until delivery, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "item2",
                    SellerId = "seller-pharmacy",
                    ProductName = "Amoxicillin 500mg",
                    Description = "Controlled antibiotic, requires a medical prescription to be presented.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "item3",
                    SellerId = "seller-digital-books",
                    ProductName = "E-book: Introduction to AI Workflows",
                    Description = "Digital PDF book, delivered by download/email after purchase, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "item4",
                    SellerId = "seller-electronics",
                    ProductName = "Bluetooth headphones",
                    Description = "Wireless headphones, ordinary physical product, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "item5",
                    SellerId = "seller-collectibles",
                    ProductName = "Limited edition sneakers",
                    Description = "Collectible sneakers, ordinary physical product, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["item1"] = new()
            {
                new SourcingCandidate("cold-dc-sp", AvailableStock: 5, LeadTimeNominalHours: 4, CurrentQueue: 0, CapacityPerHour: 10)
            },
            ["item2"] = new()
            {
                new SourcingCandidate("central-pharmacy-sp", AvailableStock: 5, LeadTimeNominalHours: 2, CurrentQueue: 0, CapacityPerHour: 10)
            },
            ["item4"] = new()
            {
                new SourcingCandidate("corner-store-sp", AvailableStock: 3, LeadTimeNominalHours: 1, CurrentQueue: 200, CapacityPerHour: 5),
                new SourcingCandidate("regional-dc-sp", AvailableStock: 40, LeadTimeNominalHours: 24, CurrentQueue: 5, CapacityPerHour: 50)
            },
            ["item5"] = new()
            {
                new SourcingCandidate("flagship-store-sp", AvailableStock: 0, LeadTimeNominalHours: 1, CurrentQueue: 0, CapacityPerHour: 5)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-frozen-treats"),
            new PaymentApprovedEvent("seller-pharmacy"),
            new PaymentApprovedEvent("seller-digital-books"),
            new PaymentApprovedEvent("seller-electronics"),
            new PaymentApprovedEvent("seller-collectibles"),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("item1"),
            new CarrierDeliveredEvent("item2"),
            new CarrierDeliveredEvent("item4"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["item1"] = new("none", false, "refrigerated_express", ItemStatus.Delivered, "cold-dc-sp"),
            ["item2"] = new("prescription", false, "standard", ItemStatus.Delivered, "central-pharmacy-sp"),
            ["item3"] = new("none", true, "standard", ItemStatus.Delivered, null),
            ["item4"] = new("none", false, "standard", ItemStatus.Delivered, "regional-dc-sp"),
            ["item5"] = new("none", false, "standard", ItemStatus.Deferred, null)
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("cold-dc-sp", "refrigerated_express", new[] { "item1" }),
            new("central-pharmacy-sp", "standard", new[] { "item2" }),
            new("regional-dc-sp", "standard", new[] { "item4" })
        };

        return new ScenarioDefinition(
            Name: "mixed-attributes",
            Description: "Regression case: prescription + digital + cold-chain + queue-effect sourcing + a deferred item, all in one order.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }

    // The flip side of multi-seller-complex: two items from two different sellers that both
    // happen to route through the same shared regional hub with the same SLA - they MUST merge
    // into a single shipment. Origin+SLA is what decides grouping, never "seller".
    private static ScenarioDefinition SameOriginMergesAcrossSellers()
    {
        var order = new Order
        {
            OrderId = "order-eval-same-origin-merge",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 200 - Campinas, SP",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "book1",
                    SellerId = "seller-books",
                    ProductName = "Paperback novel",
                    Description = "Ordinary printed book, no special handling, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "toy1",
                    SellerId = "seller-toys",
                    ProductName = "Wooden building blocks set",
                    Description = "Ordinary toy, no special handling, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["book1"] = new()
            {
                new SourcingCandidate("shared-3pl-hub-campinas", AvailableStock: 20, LeadTimeNominalHours: 8, CurrentQueue: 0, CapacityPerHour: 20)
            },
            ["toy1"] = new()
            {
                new SourcingCandidate("shared-3pl-hub-campinas", AvailableStock: 15, LeadTimeNominalHours: 8, CurrentQueue: 0, CapacityPerHour: 20)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-books"),
            new PaymentApprovedEvent("seller-toys"),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("book1"),
            new CarrierDeliveredEvent("toy1"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["book1"] = new("none", false, "standard", ItemStatus.Delivered, "shared-3pl-hub-campinas"),
            ["toy1"] = new("none", false, "standard", ItemStatus.Delivered, "shared-3pl-hub-campinas")
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("shared-3pl-hub-campinas", "standard", new[] { "book1", "toy1" })
        };

        return new ScenarioDefinition(
            Name: "same-origin-merges-across-sellers",
            Description: "Two items from different sellers that route through the same origin+SLA must merge into one shipment.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }

    // Exercises the generic blocking-requirement gate being denied. The item is cancelled before
    // sourcing ever runs, while an unrelated item from a different seller in the same order
    // still ships normally.
    private static ScenarioDefinition BlockingRequirementDenied()
    {
        var order = new Order
        {
            OrderId = "order-eval-blocking-requirement-denied",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 300 - Belo Horizonte, MG",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "deniedmed1",
                    SellerId = "seller-pharmacy-bh",
                    ProductName = "Diazepam 10mg",
                    Description = "Controlled anxiolytic medication, requires a medical prescription to be presented.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "other1",
                    SellerId = "seller-stationery-bh",
                    ProductName = "Notebook (stationery)",
                    Description = "Ordinary paper notebook, no special handling, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["deniedmed1"] = new()
            {
                new SourcingCandidate("seller-pharmacy-bh-dc", AvailableStock: 5, LeadTimeNominalHours: 2, CurrentQueue: 0, CapacityPerHour: 10)
            },
            ["other1"] = new()
            {
                new SourcingCandidate("seller-stationery-bh-wh", AvailableStock: 10, LeadTimeNominalHours: 5, CurrentQueue: 0, CapacityPerHour: 10)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-pharmacy-bh"),
            new PaymentApprovedEvent("seller-stationery-bh"),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("other1"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            // Cancelled before sourcing ever runs - no origin gets chosen.
            ["deniedmed1"] = new("prescription", false, "standard", ItemStatus.Cancelled, null),
            ["other1"] = new("none", false, "standard", ItemStatus.Delivered, "seller-stationery-bh-wh")
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("seller-stationery-bh-wh", "standard", new[] { "other1" })
        };

        return new ScenarioDefinition(
            Name: "prescription-denied",
            Description: "A prescription gets denied - that item is cancelled before sourcing runs, while the rest of the order ships normally.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments,
            DeniedBlockingRequirementItemIds: new HashSet<string> { "deniedmed1" });
    }

    // One order, five items, five different origin *types* (an express hub, a customs dock, a
    // regional warehouse, a made-to-order factory, a cold DC) and all three shipping SLAs
    // (standard, express, refrigerated_express) at once. Also exercises a sourcing decision on
    // pure lead time (dock vs. warehouse), with no queue/demand effect involved.
    private static ScenarioDefinition HeterogeneousNetwork()
    {
        var order = new Order
        {
            OrderId = "order-eval-heterogeneous-network",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 400 - Porto Alegre, RS",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "part1",
                    SellerId = "seller-bike-parts",
                    ProductName = "Bicycle brake cable",
                    Description = "Urgent replacement bicycle part, small physical item, needs fast delivery but no refrigeration, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "import1",
                    SellerId = "seller-imports",
                    ProductName = "Imported mechanical keyboard",
                    Description = "Imported physical product, ordinary shipping, no special urgency, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "factory1",
                    SellerId = "seller-custom-factory",
                    ProductName = "Custom-engraved wooden nameplate",
                    Description = "Made-to-order physical product, ordinary shipping, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "fish1",
                    SellerId = "seller-seafood",
                    ProductName = "Fresh salmon fillet",
                    Description = "Perishable seafood, must stay refrigerated and requires cold-chain fast delivery, no legal restriction.",
                    QuantityRequested = 1
                },
                new()
                {
                    ItemId = "office1",
                    SellerId = "seller-office-supplies",
                    ProductName = "Stapler",
                    Description = "Ordinary office supply, physical product, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["part1"] = new()
            {
                new SourcingCandidate("seller-parts-express-hub-poa", AvailableStock: 6, LeadTimeNominalHours: 2, CurrentQueue: 0, CapacityPerHour: 10)
            },
            // import1: the customs dock has plenty of stock but a slow customs-clearance lead
            // time - the regional warehouse wins on pure lead time, no queue effect involved.
            ["import1"] = new()
            {
                new SourcingCandidate("seller-electronics-dock-santos", AvailableStock: 30, LeadTimeNominalHours: 72, CurrentQueue: 0, CapacityPerHour: 20),
                new SourcingCandidate("seller-electronics-wh-curitiba", AvailableStock: 12, LeadTimeNominalHours: 20, CurrentQueue: 0, CapacityPerHour: 15)
            },
            ["factory1"] = new()
            {
                new SourcingCandidate("seller-custom-factory-joinville", AvailableStock: 50, LeadTimeNominalHours: 96, CurrentQueue: 0, CapacityPerHour: 5)
            },
            ["fish1"] = new()
            {
                new SourcingCandidate("seller-seafood-cold-dc-santos", AvailableStock: 10, LeadTimeNominalHours: 3, CurrentQueue: 0, CapacityPerHour: 10)
            },
            ["office1"] = new()
            {
                new SourcingCandidate("seller-office-wh-joinville", AvailableStock: 100, LeadTimeNominalHours: 10, CurrentQueue: 0, CapacityPerHour: 20)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-bike-parts"),
            new PaymentApprovedEvent("seller-imports"),
            new PaymentApprovedEvent("seller-custom-factory"),
            new PaymentApprovedEvent("seller-seafood"),
            new PaymentApprovedEvent("seller-office-supplies"),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("part1"),
            new CarrierDeliveredEvent("import1"),
            new CarrierDeliveredEvent("factory1"),
            new CarrierDeliveredEvent("fish1"),
            new CarrierDeliveredEvent("office1"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["part1"] = new("none", false, "express", ItemStatus.Delivered, "seller-parts-express-hub-poa"),
            ["import1"] = new("none", false, "standard", ItemStatus.Delivered, "seller-electronics-wh-curitiba"),
            ["factory1"] = new("none", false, "standard", ItemStatus.Delivered, "seller-custom-factory-joinville"),
            ["fish1"] = new("none", false, "refrigerated_express", ItemStatus.Delivered, "seller-seafood-cold-dc-santos"),
            ["office1"] = new("none", false, "standard", ItemStatus.Delivered, "seller-office-wh-joinville")
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("seller-parts-express-hub-poa", "express", new[] { "part1" }),
            new("seller-electronics-wh-curitiba", "standard", new[] { "import1" }),
            new("seller-custom-factory-joinville", "standard", new[] { "factory1" }),
            new("seller-seafood-cold-dc-santos", "refrigerated_express", new[] { "fish1" }),
            new("seller-office-wh-joinville", "standard", new[] { "office1" })
        };

        return new ScenarioDefinition(
            Name: "heterogeneous-network",
            Description: "Five items, five origin types (express hub, customs dock, regional warehouse, made-to-order factory, cold DC), all three SLAs at once.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }

    // A seller's payment never clears - the item is cancelled straight from
    // AwaitingPaymentApproval, before classification-driven gates or sourcing ever run.
    private static ScenarioDefinition PaymentDenied()
    {
        var order = new Order
        {
            OrderId = "order-eval-payment-denied",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 600 - Curitiba, PR",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "gadget1",
                    SellerId = "seller-flaky-gadgets",
                    ProductName = "Bluetooth speaker",
                    Description = "Ordinary bluetooth speaker, physical product, no special handling, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["gadget1"] = new()
            {
                new SourcingCandidate("seller-flaky-gadgets-wh-sp", AvailableStock: 10, LeadTimeNominalHours: 5, CurrentQueue: 0, CapacityPerHour: 10)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentDeniedEvent("seller-flaky-gadgets"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["gadget1"] = new("none", false, "standard", ItemStatus.Cancelled, null)
        };

        return new ScenarioDefinition(
            Name: "payment-denied",
            Description: "Payment never clears for the item's seller - cancelled before any classification gate or sourcing runs.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: new List<ExpectedShipment>());
    }

    // An item is deferred (no stock anywhere), then a RestockEvent arrives for its only origin -
    // sourcing re-runs for just that item and it proceeds all the way to delivery.
    private static ScenarioDefinition RestockReopensDeferredItem()
    {
        var order = new Order
        {
            OrderId = "order-eval-restock",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 700 - Florianópolis, SC",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "toy2",
                    SellerId = "seller-toystore",
                    ProductName = "Wooden toy car",
                    Description = "Ordinary wooden toy, physical product, no special handling, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["toy2"] = new()
            {
                new SourcingCandidate("toystore-wh-sp", AvailableStock: 0, LeadTimeNominalHours: 4, CurrentQueue: 0, CapacityPerHour: 10)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-toystore"),
            new FinalizeEvent(), // nothing to ship yet - toy2 is Deferred, customer gets notified
            new RestockEvent("toy2", "toystore-wh-sp", NewAvailableStock: 10),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("toy2"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["toy2"] = new("none", false, "standard", ItemStatus.Delivered, "toystore-wh-sp")
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("toystore-wh-sp", "standard", new[] { "toy2" })
        };

        return new ScenarioDefinition(
            Name: "restock-reopens-deferred-item",
            Description: "Out of stock everywhere at first (Deferred), then a RestockEvent lets sourcing find an origin and the item ships.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }

    // A handling exception (damaged in packing) excludes the failed origin; the Jev decides what
    // to do next and should re-reserve from the next-best origin.
    private static ScenarioDefinition HandlingExceptionReroutesToResourcing()
    {
        var order = new Order
        {
            OrderId = "order-eval-handling-exception",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 800 - Recife, PE",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "vase1",
                    SellerId = "seller-fragile-goods",
                    ProductName = "Ceramic vase",
                    Description = "Ordinary ceramic vase, physical product, no special shipping care required, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["vase1"] = new()
            {
                // Best on paper - gets chosen first, then "damaged in packing" during handling.
                new SourcingCandidate("fragile-origin-a-sp", AvailableStock: 5, LeadTimeNominalHours: 5, CurrentQueue: 0, CapacityPerHour: 10),
                new SourcingCandidate("fragile-origin-b-rj", AvailableStock: 5, LeadTimeNominalHours: 20, CurrentQueue: 0, CapacityPerHour: 10)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-fragile-goods"), // sourcing picks fragile-origin-a-sp
            // Origin A is excluded from now on; the Jev should re-reserve from origin B.
            new HandlingExceptionEvent("vase1", "damaged during packing"),
            new FinalizeEvent(), // ships from fragile-origin-b-rj
            new CarrierDeliveredEvent("vase1"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["vase1"] = new("none", false, "standard", ItemStatus.Delivered, "fragile-origin-b-rj")
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("fragile-origin-b-rj", "standard", new[] { "vase1" })
        };

        return new ScenarioDefinition(
            Name: "handling-exception-reroutes-to-resourcing",
            Description: "Item is damaged during packing at its first-choice origin - re-sourcing reroutes it to the next-best origin instead.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }

    // A delivered item gets a return request - it moves to Returned and the order is refunded,
    // the one post-delivery path no other scenario reaches.
    private static ScenarioDefinition ReturnAfterDelivery()
    {
        var order = new Order
        {
            OrderId = "order-eval-return",
            CustomerEmail = "eval@example.com",
            ShippingAddress = "Rua Exemplo, 900 - Fortaleza, CE",
            Items = new List<OrderItem>
            {
                new()
                {
                    ItemId = "watch1",
                    SellerId = "seller-timepieces",
                    ProductName = "Wristwatch",
                    Description = "Ordinary wristwatch, physical product, no special handling, no delivery urgency, no legal restriction.",
                    QuantityRequested = 1
                }
            }
        };

        var candidates = new Dictionary<string, List<SourcingCandidate>>
        {
            ["watch1"] = new()
            {
                new SourcingCandidate("timepieces-wh-sp", AvailableStock: 5, LeadTimeNominalHours: 6, CurrentQueue: 0, CapacityPerHour: 10)
            }
        };

        var events = new List<OrderEvent>
        {
            new PaymentApprovedEvent("seller-timepieces"),
            new FinalizeEvent(),
            new CarrierDeliveredEvent("watch1"),
            new ReturnRequestedEvent("watch1", "customer changed their mind"),
            new FinalizeEvent()
        };

        var expectedItems = new Dictionary<string, ExpectedItemOutcome>
        {
            ["watch1"] = new("none", false, "standard", ItemStatus.Returned, "timepieces-wh-sp")
        };

        var expectedShipments = new List<ExpectedShipment>
        {
            new("timepieces-wh-sp", "standard", new[] { "watch1" })
        };

        return new ScenarioDefinition(
            Name: "return-after-delivery",
            Description: "A delivered item is returned - the one post-delivery path (Returned + refund) no other scenario reaches.",
            Order: order,
            SourcingCandidatesByItemId: candidates,
            Events: events,
            ExpectedItemOutcomes: expectedItems,
            ExpectedShipments: expectedShipments);
    }
}
