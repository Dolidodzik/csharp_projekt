namespace PokerApp;

public sealed class StandaloneReplayListItem
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public string DisplayLabel { get; init; } = "";

    public int MaxPot { get; init; }

    public string RowBackground { get; init; } = "#1a2230";
}

public sealed class TournamentSeriesListItem
{
    public int Id { get; init; }

    public string SeriesName { get; init; } = "";

    public string DisplayLabel { get; init; } = "";
}

public sealed class SeriesHandReplayItem
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public string DisplayLabel { get; init; } = "";

    public int MaxPot { get; init; }

    public string RowBackground { get; init; } = "#1a2230";
}
