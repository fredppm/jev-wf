using JevWf.Decisions;
using JevWf.Orders.Models;
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
        }
    }
};

// Fake stock: item4 has no availability on purpose, to exercise the partial shipment path.
var stock = new Dictionary<string, int>
{
    ["item1"] = 5,
    ["item2"] = 5,
    ["item4"] = 0
};

using var decisionsClient = new DecisionsClient(apiKey);
var backend = new FakeOrderBackend(stock);
var runner = new OrderWorkflowRunner(decisionsClient, backend);

await runner.RunAsync(order);

Console.WriteLine("== Execution log ==");
foreach (var entry in backend.Log)
    Console.WriteLine($"- {entry}");

Console.WriteLine();
Console.WriteLine("== Final item status ==");
foreach (var item in order.Items)
    Console.WriteLine($"- {item.ItemId} ({item.ProductName}): {item.Status}");

return 0;
