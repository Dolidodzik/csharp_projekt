namespace PokerApp;

public sealed class SeriesTournament
{
    public int Id { get; set; }

    public int TournamentSeriesId { get; set; }

    public TournamentSeries TournamentSeries { get; set; } = null!;

    public int TournamentIndex { get; set; }

    public ICollection<SavedHand> SavedHands { get; } = new List<SavedHand>();
}
