namespace PokerApp;

public sealed class TournamentSeriesSetupPreferences
{
    public int Id { get; set; }

    public string OptionsJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}
