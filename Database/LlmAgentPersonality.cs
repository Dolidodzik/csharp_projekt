namespace PokerApp;

public sealed class LlmAgentPersonality
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PersonalityDescription { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset EditedAt { get; set; }
}
