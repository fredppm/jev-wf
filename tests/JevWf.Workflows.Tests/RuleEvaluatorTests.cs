using FluentAssertions;
using JevWf.Workflows.Building;
using JevWf.Workflows.Orders;

namespace JevWf.Workflows.Tests;

public sealed class RuleEvaluatorTests
{
    private static readonly Order Order = new("o1", "seller-a",
    [
        new OrderItem("i1", "Watch", "A watch.", 2, 60m),
        new OrderItem("i2", "Strap", "A strap.", 1, 0m)
    ]);

    [Theory]
    [InlineData("order.total > 100", true)]
    [InlineData("order.total > 120", false)]
    [InlineData("order.total >= 120", true)]
    [InlineData("order.total == 0", false)]
    [InlineData("order.itemCount == 2", true)]
    [InlineData("order.sellerId == 'seller-a'", true)]
    [InlineData("order.sellerId != 'seller-a'", false)]
    [InlineData("order.total > 100 && order.itemCount < 2", false)]
    public void Evaluates_order_rules(string rule, bool expected) =>
        RuleEvaluator.Evaluate(rule, RuleEvaluator.Fields(Order, null)).Should().Be(expected);

    [Theory]
    [InlineData("item.quantity >= 2 && item.total == 120", true)]
    [InlineData("item.unitPrice < 50", false)]
    public void Evaluates_item_rules(string rule, bool expected) =>
        RuleEvaluator.Evaluate(rule, RuleEvaluator.Fields(Order, Order.Items[0])).Should().Be(expected);

    [Theory]
    [InlineData("order.weight > 1")]
    [InlineData("order.total ~ 1")]
    [InlineData("order.sellerId > 'a'")]
    public void Rejects_invalid_rules(string rule)
    {
        var evaluate = () => RuleEvaluator.Evaluate(rule, RuleEvaluator.Fields(Order, null));

        evaluate.Should().Throw<FormatException>();
    }
}
