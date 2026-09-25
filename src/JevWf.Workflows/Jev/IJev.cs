using System.Text.Json.Nodes;

namespace JevWf.Workflows.Jev;

public abstract record JevQuestion(string Instructions);

public sealed record YesNoQuestion(string Instructions, string TrueWhen, string FalseWhen) : JevQuestion(Instructions);

// Options: piece name -> description.
public sealed record ChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string> Options) : JevQuestion(Instructions);

public sealed record JevAnswer(bool? Yes = null, string? Choice = null, double Confidence = 1);

public interface IJev
{
    // Answers every question about the same state in one call; keys are the question ids.
    Task<IReadOnlyDictionary<string, JevAnswer>> AskAsync(
        JsonObject state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken ct = default);
}
