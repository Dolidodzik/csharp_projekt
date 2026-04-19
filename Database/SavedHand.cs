namespace PokerApp;

public sealed class SavedHand
{
    public int Id { get; set; }

    public string HandName { get; set; } = string.Empty;

    public string HandHistoryJson { get; set; } = string.Empty;

    public string HandTimeIso { get; set; } = string.Empty;

    public ICollection<HandPlayer> HandPlayers { get; } = new List<HandPlayer>();
}
