using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public static class TournamentSeriesStats
{
    public static async Task RecomputeAndSaveAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var series = await db.TournamentSeries
            .FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken);
        if (series is null)
            return;

        var hands = await db.SavedHands
            .AsNoTracking()
            .Where(h => h.TournamentSeriesId == seriesId)
            .ToListAsync(cancellationToken);

        var stMap = await db.SeriesTournaments
            .AsNoTracking()
            .Where(s => s.TournamentSeriesId == seriesId)
            .ToDictionaryAsync(s => s.Id, s => s.TournamentIndex, cancellationToken);

        var snap = SeriesHandGrouping.ComputeStats(hands, stMap);
        series.StatsTournamentCount = snap.TournamentCount;
        series.StatsWinsByPlayerJson = JsonSerializer.Serialize(snap.TournamentWinsByPlayer);
        series.StatsAvgHandsPerTournament = snap.AvgHandsPerTournament;
        series.StatsAvgPromptsPerHand = snap.AvgPromptsPerHand;
        series.StatsAvgPotPerHand = snap.AvgPotPerHand;
        series.StatsAvgTournamentDurationSeconds = snap.AvgTournamentDurationSeconds;
        await db.SaveChangesAsync(cancellationToken);
    }

    public static string FormatStatsForDisplay(
        TournamentSeries s,
        IReadOnlyList<SavedHand>? hands = null,
        IReadOnlyDictionary<int, int>? seriesTournamentIdToIndex = null)
    {
        SeriesHandGrouping.SeriesStatsSnapshot snap;
        if (hands is { Count: > 0 })
        {
            snap = SeriesHandGrouping.ComputeStats(hands, seriesTournamentIdToIndex);
        }
        else
        {
            Dictionary<string, int>? wins = null;
            try
            {
                wins = JsonSerializer.Deserialize<Dictionary<string, int>>(s.StatsWinsByPlayerJson);
            }
            catch
            {
            }

            wins ??= new Dictionary<string, int>(StringComparer.Ordinal);
            snap = new SeriesHandGrouping.SeriesStatsSnapshot(
                s.StatsTournamentCount,
                wins,
                s.StatsAvgHandsPerTournament,
                s.StatsAvgPromptsPerHand,
                s.StatsAvgPotPerHand,
                s.StatsAvgTournamentDurationSeconds);
        }

        var total = snap.TournamentCount;
        var playerLines = snap.TournamentWinsByPlayer.Keys
            .OrderByDescending(p => snap.TournamentWinsByPlayer.GetValueOrDefault(p))
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p =>
            {
                var tw = snap.TournamentWinsByPlayer.GetValueOrDefault(p);
                return $"{p} {tw}/{total}";
            })
            .ToList();

        var body = playerLines.Count == 0
            ? string.Empty
            : string.Join('\n', playerLines) + '\n';

        return
            $"Turnieje: {total}\n" +
            body +
            $"Śr. rozdań/turniej: {snap.AvgHandsPerTournament:0.##}\n" +
            $"Śr. promptów/ręka: {snap.AvgPromptsPerHand:0.##}\n" +
            $"Śr. pula/ręka: {snap.AvgPotPerHand:0.##}\n" +
            $"Śr. czas turnieju: {snap.AvgTournamentDurationSeconds:0.##} s";
    }
}
