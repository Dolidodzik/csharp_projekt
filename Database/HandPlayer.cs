namespace PokerApp;

public sealed class HandPlayer
{
    public int Id { get; set; }

    public int HandId { get; set; }

    public SavedHand SavedHand { get; set; } = null!;

    public string PlayerName { get; set; } = string.Empty;

    public string PlayerType { get; set; } = string.Empty;

    public int? LlmPersonalityId { get; set; }

    public LlmAgentPersonality? LlmPersonality { get; set; }
}
