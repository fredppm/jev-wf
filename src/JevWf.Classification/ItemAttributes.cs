namespace JevWf.Classification;

// Attributes resolved by the Jev from an item's free-text name/description.
// BlockingRequirementType is "none" or a specific requirement (e.g. "prescription",
// "age_restricted") - generic on purpose, so a new requirement type is a new Choice option,
// not a new question/field. Confidence is the weakest-link confidence across this item's
// classification questions (0-1), used by OrderWorkflowRunner to decide whether to escalate.
public sealed record ItemAttributes(
    string BlockingRequirementType,
    bool IsDigitalOnly,
    string ShippingMethod,
    double Confidence);
