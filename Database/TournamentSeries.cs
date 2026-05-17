namespace PokerApp;

public sealed class TournamentSeries
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public int StatsTournamentCount { get; set; }

    public string StatsWinsByPlayerJson { get; set; } = "{}";

    public double StatsAvgHandsPerTournament { get; set; }

    public double StatsAvgPromptsPerHand { get; set; }

    public double StatsAvgPotPerHand { get; set; }

    public double StatsAvgTournamentDurationSeconds { get; set; }

    public ICollection<SeriesTournament> SeriesTournaments { get; } = new List<SeriesTournament>();

    public ICollection<SavedHand> SavedHands { get; } = new List<SavedHand>();
}
