using System.Globalization;
using System.Text.RegularExpressions;
using JevWf.Workflows.Orders;

namespace JevWf.Workflows.Building;

// Deterministic piece conditions: comparisons joined by "&&", e.g. "order.total > 100 && item.quantity >= 2".
public static partial class RuleEvaluator
{
    public static bool Evaluate(string rule, IReadOnlyDictionary<string, object> fields) =>
        rule.Split("&&").All(comparison => EvaluateComparison(comparison, fields));

    public static IReadOnlyDictionary<string, object> Fields(Order order, OrderItem? item)
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["order.total"] = order.Total,
            ["order.sellerId"] = order.SellerId,
            ["order.itemCount"] = (decimal)order.Items.Count
        };

        if (item is not null)
        {
            fields["item.unitPrice"] = item.UnitPrice;
            fields["item.quantity"] = (decimal)item.Quantity;
            fields["item.total"] = item.Total;
        }

        return fields;
    }

    private static bool EvaluateComparison(string text, IReadOnlyDictionary<string, object> fields)
    {
        var match = ComparisonPattern().Match(text);
        if (!match.Success)
            throw new FormatException($"Invalid rule '{text.Trim()}'.");

        var field = match.Groups["field"].Value;
        var op = match.Groups["op"].Value;
        var literal = match.Groups["value"].Value;

        if (!fields.TryGetValue(field, out var left))
            throw new FormatException($"Unknown rule field '{field}'.");

        if (left is decimal number)
        {
            var right = decimal.Parse(literal, CultureInfo.InvariantCulture);
            return op switch
            {
                ">" => number > right,
                ">=" => number >= right,
                "<" => number < right,
                "<=" => number <= right,
                "==" => number == right,
                _ => number != right
            };
        }

        var equal = string.Equals((string)left, literal.Trim('\'', '"'), StringComparison.Ordinal);
        return op switch
        {
            "==" => equal,
            "!=" => !equal,
            _ => throw new FormatException($"Operator '{op}' needs a numeric field, but '{field}' is text.")
        };
    }

    [GeneratedRegex(@"^\s*(?<field>[A-Za-z_][\w.]*)\s*(?<op>>=|<=|==|!=|>|<)\s*(?<value>.+?)\s*$")]
    private static partial Regex ComparisonPattern();
}
