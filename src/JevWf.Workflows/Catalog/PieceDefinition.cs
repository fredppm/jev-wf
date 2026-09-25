using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JevWf.Workflows.Catalog;

public enum PieceKind
{
    // Always part of the workflow (unless its rule does not pass).
    Fixed,
    // Must be part of the workflow whenever its condition holds.
    Required,
    // Enters when its condition holds, or competes with other pieces at the same point.
    Optional
}

public enum PieceScope
{
    Order,
    Item
}

// "rule" is evaluated by code; "jev" is a statement the Jev judges and must hold; "jevNot" is a
// statement that must not hold. Two pieces with the same statement, one as "jev" and the other as
// "jevNot", are exclusive alternatives decided by one question. The rule and the statement must both pass.
public sealed record Condition(string? Rule = null, string? Jev = null, string? JevNot = null)
{
    public string? Statement => Jev ?? JevNot;
}

public sealed record PieceDefinition
{
    public const string ExceptionPort = "exception";

    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public PieceKind Kind { get; init; } = PieceKind.Fixed;
    public PieceScope Scope { get; init; } = PieceScope.Item;
    public Condition? When { get; init; }

    [JsonConverter(typeof(StringOrArrayConverter))]
    public IReadOnlyList<string> In { get; init; } = [];

    // Port name -> type it produces.
    public IReadOnlyDictionary<string, string> Out { get; init; } = new Dictionary<string, string>();

    // Type produced when the piece fails; the catalog's default exception when omitted.
    public string? OnException { get; init; }

    // Name of the VTEX piece this one (and the rest of its chain) replaces.
    public string? Replaces { get; init; }

    public string? PublicStatus { get; init; }

    // Picked when several pieces compete and the Jev is not deciding.
    public bool Default { get; init; }

    public JsonObject? Config { get; init; }

    // "vtex" or the seller id that defined the piece.
    [JsonIgnore]
    public string Source { get; init; } = PieceCatalog.VtexSource;
}

public sealed class StringOrArrayConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? [reader.GetString()!]
            : JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? [];

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        if (value.Count == 1)
            writer.WriteStringValue(value[0]);
        else
            JsonSerializer.Serialize(writer, value, options);
    }
}
