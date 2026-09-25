using FluentAssertions;
using JevWf.Workflows.Building;
using JevWf.Workflows.Catalog;
using JevWf.Workflows.Jev;
using JevWf.Workflows.Orders;
using JevWf.Workflows.Output;

namespace JevWf.Workflows.Tests;

public sealed class WorkflowBuilderTests
{
    private static readonly PieceCatalog Catalog = PieceCatalog.Load(TestPaths.Catalog);

    [Fact]
    public async Task Payment_is_left_out_when_the_order_costs_nothing()
    {
        var workflow = await Build(Order(unitPrice: 0));

        workflow.Nodes.Select(n => n.Piece).Should().NotContain("payment");
        workflow.Edges.Should().Contain(Edge("confirm_seller", "confirmed", "order_confirmed", "release_items"));
    }

    [Fact]
    public async Task Fraud_check_and_its_score_run_before_payment_above_100()
    {
        var workflow = await Build(Order(unitPrice: 150));

        workflow.Edges.Should().Contain(
        [
            Edge("confirm_seller", "confirmed", "order_confirmed", "fraud_check"),
            Edge("fraud_check", "scored", "fraud_scored", "evaluate_fraud_score"),
            Edge("evaluate_fraud_score", "approve", "order_confirmed", "payment"),
            Edge("evaluate_fraud_score", "review", "manual_review_required", null),
            Edge("payment", "approved", "order_confirmed", "release_items")
        ]);
    }

    [Fact]
    public async Task Prescription_is_requested_before_stock_when_the_jev_says_so()
    {
        var jev = Jev(("o1/i1", "request_prescription", new JevAnswer(Yes: true, Confidence: 0.95)));

        var workflow = await Build(Order(), jev);

        workflow.Edges.Should().Contain(
        [
            Edge("release_items", "released", "item_released", "i1/request_prescription"),
            Edge("i1/request_prescription", "approved", "item_released", "i1/reserve_stock"),
            Edge("i1/request_prescription", "denied", "item_cancelled", null)
        ]);
    }

    [Fact]
    public async Task The_jev_picks_between_pieces_competing_for_the_same_type()
    {
        var jev = Jev(("o1/i1", "next_after_item_released", new JevAnswer(Choice: "deliver_digital", Confidence: 0.9)));

        var workflow = await Build(Order(), jev);

        workflow.Mode.Should().Be(WorkflowMode.Jev);
        workflow.Nodes.Select(n => n.Id).Should().BeEquivalentTo("confirm_seller", "payment", "release_items", "i1/deliver_digital");
    }

    [Fact]
    public async Task Exceptions_and_restocks_loop_back_to_sourcing()
    {
        var workflow = await Build(Order());

        workflow.Edges.Should().Contain(
        [
            Edge("i1/start_handling", "exception", "resourcing_required", "i1/reserve_stock"),
            Edge("i1/wait_delivery", "exception", "resourcing_required", "i1/reserve_stock"),
            Edge("i1/reserve_stock", "no_stock", "awaiting_restock", "i1/wait_restock"),
            Edge("i1/wait_restock", "restocked", "restock_received", "i1/reserve_stock")
        ]);
    }

    [Fact]
    public async Task Pieces_without_exception_handling_go_to_manual_review()
    {
        var workflow = await Build(Order());

        workflow.Edges.Should().Contain(Edge("i1/reserve_stock", "exception", "manual_review_required", null));
    }

    [Fact]
    public async Task Each_item_gets_its_own_branch()
    {
        var order = new Order("o1", "seller-a",
        [
            new OrderItem("i1", "Ice cream", "Frozen dessert.", 1, 20m),
            new OrderItem("i2", "Spoon", "Plastic spoon.", 1, 1m)
        ]);
        var jev = Jev(("o1/i1", "next_after_ready_to_ship", new JevAnswer(Choice: "ship_cold_chain", Confidence: 0.9)));

        var workflow = await Build(order, jev);

        workflow.Edges.Should().Contain(
        [
            Edge("release_items", "released", "item_released", "i1/reserve_stock"),
            Edge("release_items", "released", "item_released", "i2/reserve_stock"),
            Edge("i1/issue_invoice", "issued", "ready_to_ship", "i1/ship_cold_chain"),
            Edge("i2/issue_invoice", "issued", "ready_to_ship", "i2/ship_standard")
        ]);
    }

    [Fact]
    public async Task Gates_at_the_same_type_run_in_catalog_order()
    {
        var order = Order() with { Items = [new OrderItem("i1", "Zolpidem", "Controlled medicine.", 3, 40m)] };
        var jev = Jev(
            ("o1/i1", "request_prescription", new JevAnswer(Yes: true, Confidence: 0.95)),
            ("o1/i1", "pharmacist_review", new JevAnswer(Yes: true, Confidence: 0.95)));

        var workflow = await Build(order, jev);

        workflow.Edges.Should().Contain(
        [
            Edge("i1/request_prescription", "approved", "item_released", "i1/pharmacist_review"),
            Edge("i1/pharmacist_review", "approved", "item_released", "i1/reserve_stock")
        ]);
    }

    [Fact]
    public async Task Pharmacist_review_needs_both_its_rule_and_its_statement()
    {
        var jev = Jev(
            ("o1/i1", "request_prescription", new JevAnswer(Yes: true, Confidence: 0.95)),
            ("o1/i1", "pharmacist_review", new JevAnswer(Yes: true, Confidence: 0.95)));

        var workflow = await Build(Order(), jev);

        workflow.Nodes.Select(n => n.Piece).Should().Contain("request_prescription").And.NotContain("pharmacist_review");
    }

    [Fact]
    public async Task An_order_level_statement_is_judged_on_the_whole_order()
    {
        var jev = Jev(("o1", "review_unusual_order", new JevAnswer(Yes: true, Confidence: 0.9)));

        var workflow = await Build(Order(), jev);

        workflow.Edges.Should().Contain(
        [
            Edge("confirm_seller", "confirmed", "order_confirmed", "review_unusual_order"),
            Edge("review_unusual_order", "approved", "order_confirmed", "payment"),
            Edge("review_unusual_order", "rejected", "order_cancelled", null)
        ]);
    }

    [Fact]
    public async Task Special_transport_is_booked_after_the_invoice_and_before_shipping()
    {
        var jev = Jev(("o1/i1", "require_special_transport", new JevAnswer(Yes: true, Confidence: 0.95)));

        var workflow = await Build(Order(), jev);

        workflow.Edges.Should().Contain(
        [
            Edge("i1/start_handling", "ready", "ready_to_ship", "i1/issue_invoice"),
            Edge("i1/issue_invoice", "issued", "ready_to_ship", "i1/require_special_transport"),
            Edge("i1/require_special_transport", "declared", "ready_to_ship", "i1/ship_standard")
        ]);
    }

    [Fact]
    public async Task Made_to_order_items_are_produced_instead_of_reserved()
    {
        var jev = Jev(
            ("o1/i1", "next_after_item_released", new JevAnswer(Choice: "produce_to_order", Confidence: 0.9)),
            ("o1/i1", "return_window", new JevAnswer(Yes: false, Confidence: 0.9)));

        var workflow = await Build(Order(), jev);

        workflow.Edges.Should().Contain(
        [
            Edge("release_items", "released", "item_released", "i1/produce_to_order"),
            Edge("i1/produce_to_order", "produced", "stock_reserved", "i1/start_handling"),
            Edge("i1/wait_delivery", "delivered", "item_delivered", "i1/close_without_return")
        ]);
        workflow.Nodes.Select(n => n.Piece).Should().NotContain("return_window");
    }

    [Fact]
    public async Task A_seller_can_add_a_piece_without_replacing_any()
    {
        var order = Order() with { SellerId = "seller-gift-shop" };
        var jev = Jev(("o1/i1", "gift_wrap", new JevAnswer(Yes: true, Confidence: 0.9)));

        var workflow = await Build(order, jev);

        workflow.Edges.Should().Contain(
        [
            Edge("i1/reserve_stock", "reserved", "stock_reserved", "i1/gift_wrap"),
            Edge("i1/gift_wrap", "wrapped", "stock_reserved", "i1/start_handling")
        ]);
    }

    [Theory]
    [InlineData(0.69)]
    [InlineData(0.2)]
    public async Task A_low_confidence_answer_gives_the_default_workflow(double confidence)
    {
        var jev = Jev(("o1/i1", "next_after_item_released", new JevAnswer(Choice: "deliver_digital", Confidence: confidence)));

        var workflow = await Build(Order(), jev);

        workflow.Mode.Should().Be(WorkflowMode.Default);
        workflow.Nodes.Select(n => n.Piece).Should().Contain(["request_prescription", "reserve_stock", "ship_standard"])
            .And.NotContain("deliver_digital");
    }

    [Fact]
    public async Task A_choice_outside_the_options_gives_the_default_workflow()
    {
        var jev = Jev(("o1/i1", "next_after_item_released", new JevAnswer(Choice: "teleport", Confidence: 0.99)));

        var workflow = await Build(Order(), jev);

        workflow.Mode.Should().Be(WorkflowMode.Default);
    }

    [Fact]
    public async Task A_custom_chain_replaces_the_vtex_piece_in_the_workflow()
    {
        var order = Order() with { SellerId = "seller-pharmacy-express" };

        var workflow = await Build(order);

        workflow.Nodes.Select(n => n.Piece).Should().NotContain("start_handling");
        workflow.Edges.Should().Contain(
        [
            Edge("i1/reserve_stock", "reserved", "stock_reserved", "i1/pick_from_shelf"),
            Edge("i1/pack", "exception", "pack_failed", "i1/repack"),
            Edge("i1/pick_from_shelf", "exception", "resourcing_required", "i1/reserve_stock"),
            Edge("i1/pack", "packed", "ready_to_ship", "i1/issue_invoice")
        ]);
        workflow.Nodes.Single(n => n.Piece == "pack").PublicStatus.Should().Be("handling");
    }

    [Fact]
    public async Task A_custom_piece_producing_a_type_nothing_accepts_is_rejected()
    {
        var catalog = PieceCatalogTests.WithSeller(
            PieceCatalogTests.Piece("gift_wrap", "stock_reserved", ("wrapped", "wrapped")) with { PublicStatus = "handling", Kind = PieceKind.Optional });
        var order = Order() with { SellerId = "seller-x" };
        var jev = Jev(("o1/i1", "next_after_stock_reserved", new JevAnswer(Choice: "gift_wrap", Confidence: 0.99)));

        var build = () => new WorkflowBuilder(catalog, jev).BuildAsync(order);

        // The Jev's workflow is invalid, so the default one is used, and it picks start_handling.
        (await build()).Mode.Should().Be(WorkflowMode.Default);
    }

    [Fact]
    public async Task Every_order_of_the_group_gets_its_own_workflow()
    {
        var group = new OrderGroup("g1", [Order(), Order() with { OrderId = "o2", SellerId = "seller-b" }]);

        var result = await new WorkflowBuilder(Catalog, Jev()).BuildAsync(group);

        result.Workflows.Select(w => w.OrderId).Should().Equal("o1", "o2");
    }

    private static Order Order(decimal unitPrice = 50m) =>
        new("o1", "seller-a", [new OrderItem("i1", "Product", "A product.", 1, unitPrice)]);

    private static ScriptedJev Jev(params (string Key, string Question, JevAnswer Answer)[] answers) =>
        new(answers.GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.ToDictionary(a => a.Question, a => a.Answer)));

    private static Task<OrderWorkflow> Build(Order order, IJev? jev = null) =>
        new WorkflowBuilder(Catalog, jev ?? Jev()).BuildAsync(order);

    private static WorkflowEdge Edge(string from, string port, string type, string? to) => new(from, port, type, to);
}
