namespace PokerApp;

public sealed class TournamentSeriesSetupOptionsPayload
{
    public string SeriesName { get; set; } = "";

    public int TournamentCount { get; set; } = 5;

    public int BuyIn { get; set; }

    public int SmallBlind { get; set; }

    public int BotCount { get; set; } = 4;

    public List<TournamentSeriesSetupBotPayload> Bots { get; set; } = [];
}

public sealed class TournamentSeriesSetupBotPayload
{
    public string Type { get; set; } = "RandomBotPlayer";

    public string Name { get; set; } = "";

    public int? PersonalityId { get; set; }

    public int? PresetId { get; set; }
}
