using System.Linq;

namespace PokerApp;

public enum BotType
{
    RandomBotPlayer,
    LlmBotPlayer
}

public sealed record LlmPersonalitySnapshot(int? Id, string Name, string Description);

public sealed record BotSetup(string Name, BotType Type, LlmPersonalitySnapshot? LlmPersonality = null);

public sealed record GameSetupConfig(
    int BuyIn,
    int SmallBlind,
    IReadOnlyList<BotSetup> Bots,
    string? LlmApiUrl = null,
    string? LlmApiKey = null,
    string LlmModel = "qwen/qwen3-32b",
    double LlmTemperature = 0)
{
    public int BigBlind => SmallBlind * 2;
    public bool UsesLlm => Bots.Any(b => b.Type == BotType.LlmBotPlayer);

    public static GameSetupConfig CreateDefault() =>
        new(
            GameConstants.DefaultBuyIn,
            GameConstants.DefaultSmallBlind,
            [
                new BotSetup("Bot A", BotType.RandomBotPlayer),
                new BotSetup("Bot B", BotType.RandomBotPlayer),
                new BotSetup("Bot C", BotType.RandomBotPlayer),
            ],
            LlmApiUrl: "https://api.groq.com/openai/v1",
            LlmApiKey: "",
            LlmModel: "qwen/qwen3-32b");
}
