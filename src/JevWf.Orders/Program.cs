using JevWf.Classification;
using JevWf.Decisions;
using JevWf.Orders.Models;
using JevWf.Orders.Sourcing;
using JevWf.Orders.Workflow;

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set the OPENROUTER_API_KEY environment variable with your OpenRouter key before running.");
    return 1;
}

var order = new Order
{
    OrderId = "order-001",
    CustomerEmail = "customer@example.com",
    ShippingAddress = "123 Example St - São Paulo, SP",
    Items = new List<OrderItem>
    {
        new()
        {
            ItemId = "item1",
            ProductName = "Pistachio ice cream 1L",
            Description = "Artisanal pistachio ice cream tub, must be kept frozen until delivery.",
            QuantityRequested = 1
        },
        new()
        {
            ItemId = "item2",
            ProductName = "Amoxicillin 500mg",
            Description = "Controlled antibiotic, requires a medical prescription to be presented.",
            QuantityRequested = 1
        },
        new()
        {
            ItemId = "item3",
            ProductName = "E-book: Introduction to AI Workflows",
            Description = "Digital PDF book, delivered by download/email after purchase.",
            QuantityRequested = 1
        },
        new()
        {
            ItemId = "item4",
            ProductName = "Bluetooth headphones",
            Description = "Wireless headphones, ordinary physical product, no delivery urgency.",
            QuantityRequested = 1
        },
        new()
        {
            ItemId = "item5",
            ProductName = "Limited edition sneakers",
            Description = "Collectible sneakers, ordinary physical product, no delivery urgency.",
            QuantityRequested = 1
        }
    }
};

// Step 1: resolve each item's attributes by asking the Jev (the only step that talks to it).
using var decisionsClient = new DecisionsClient(apiKey);
var classifier = new OrderItemClassifier(decisionsClient);

var attributes = await classifier.ClassifyAsync(
    order.OrderId,
    order.Items.Select(i => (i.ItemId, i.ProductName, i.Description)).ToList());

foreach (var item in order.Items)
{
    var resolved = attributes[item.ItemId];
    item.RequiresPrescription = resolved.RequiresPrescription;
    item.IsDigitalOnly = resolved.IsDigitalOnly;
    item.ShippingMethod = resolved.ShippingMethod;
}

// Step 2: run the deterministic workflow, which never talks to the Jev.
var inventory = new FakeInventoryCatalog(new Dictionary<string, List<SourcingCandidate>>
{
    ["item1"] = new()
    {
        new SourcingCandidate("cold-dc-sp", AvailableStock: 5, LeadTimeNominalHours: 4, CurrentQueue: 0, CapacityPerHour: 10)
    },
    ["item2"] = new()
    {
        new SourcingCandidate("central-pharmacy-sp", AvailableStock: 5, LeadTimeNominalHours: 2, CurrentQueue: 0, CapacityPerHour: 10)
    },
    // item4: the corner store is nominally faster (1h) but is swamped right now (200-order
    // queue at 5/hour = 40h backlog), so its *effective* lead time loses to the regional DC.
    ["item4"] = new()
    {
        new SourcingCandidate("corner-store-sp", AvailableStock: 3, LeadTimeNominalHours: 1, CurrentQueue: 200, CapacityPerHour: 5),
        new SourcingCandidate("regional-dc-sp", AvailableStock: 40, LeadTimeNominalHours: 24, CurrentQueue: 5, CapacityPerHour: 50)
    },
    // item5: every candidate is out of stock -> sourcing finds nothing -> the item is deferred.
    ["item5"] = new()
    {
        new SourcingCandidate("flagship-store-sp", AvailableStock: 0, LeadTimeNominalHours: 1, CurrentQueue: 0, CapacityPerHour: 5)
    }
});

var backend = new FakeOrderBackend();
var runner = new OrderWorkflowRunner(backend, inventory);
runner.Run(order);

Console.WriteLine("== Execution log ==");
foreach (var entry in backend.Log)
    Console.WriteLine($"- {entry}");

Console.WriteLine();
Console.WriteLine("== Final item status ==");
foreach (var item in order.Items)
    Console.WriteLine($"- {item.ItemId} ({item.ProductName}): {item.Status} [origin={item.ChosenOriginId ?? "-"}]");

return 0;
