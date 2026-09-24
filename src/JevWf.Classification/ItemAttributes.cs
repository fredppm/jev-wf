namespace JevWf.Classification;

// Attributes resolved by the Jev from an item's free-text name/description.
public sealed record ItemAttributes(
    bool RequiresPrescription,
    bool IsDigitalOnly,
    string ShippingMethod);
