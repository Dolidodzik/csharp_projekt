namespace PokerApp;

public sealed class OpenAiPreset
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ApiUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset EditedAt { get; set; }
}
