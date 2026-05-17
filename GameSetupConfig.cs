using System.Linq;

namespace PokerApp;

public enum BotType
{
    RandomBotPlayer,
    LlmBotPlayer
}

public sealed record LlmPersonalitySnapshot(int? Id, string Name, string Description);

public sealed record OpenAiPresetSnapshot(int Id, string Name, string ApiUrl, string ApiKey, string ModelName);

public sealed record BotSetup(
    string Name,
    BotType Type,
    LlmPersonalitySnapshot? LlmPersonality = null,
    OpenAiPresetSnapshot? OpenAiPreset = null);

public sealed record GameSetupConfig(
    int BuyIn,
    int SmallBlind,
    IReadOnlyList<BotSetup> Bots,
    double LlmTemperature = 0,
    bool SpectatorSeriesMode = false,
    int? SeriesTournamentNumber = null,
    int? SeriesTournamentTotal = null)
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
            LlmTemperature: 0);
}
