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
            .Include(s => s.SeriesTournaments)
            .FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken);
        if (series is null)
            return;

        var tournamentCount = series.SeriesTournaments.Count;
        var hands = await db.SavedHands
            .AsNoTracking()
            .Where(h => h.TournamentSeriesId == seriesId)
            .ToListAsync(cancellationToken);

        if (tournamentCount == 0 || hands.Count == 0)
        {
            series.StatsTournamentCount = tournamentCount;
            series.StatsWinsByPlayerJson = "{}";
            series.StatsAvgHandsPerTournament = 0;
            series.StatsAvgPromptsPerHand = 0;
            series.StatsAvgPotPerHand = 0;
            series.StatsAvgTournamentDurationSeconds = 0;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var totalHands = hands.Count;
        var totalPrompts = hands.Sum(h => ReplayJsonUtil.CountPromptsFromReplayJson(h.HandHistoryJson));
        var totalPot = hands.Sum(h => (double)h.MaxPot);

        var wins = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var st in series.SeriesTournaments)
        {
            var th = hands.Where(h => h.SeriesTournamentId == st.Id).ToList();
            if (th.Count == 0)
                continue;
            var ordered = th
                .OrderBy(h => DateTimeOffset.TryParse(h.HandTimeIso, out var dto) ? dto : DateTimeOffset.MinValue)
                .ThenBy(h => h.Id)
                .ToList();
            string? winner = null;
            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                if (ReplayJsonUtil.TournamentFinishedInReplayJson(ordered[i].HandHistoryJson))
                {
                    winner = ReplayJsonUtil.TournamentWinnerFromReplayJson(ordered[i].HandHistoryJson);
                    break;
                }
            }

            winner ??= ReplayJsonUtil.WinnersFromReplayJson(ordered[^1].HandHistoryJson).FirstOrDefault();
            if (!string.IsNullOrEmpty(winner))
            {
                wins.TryGetValue(winner, out var wc);
                wins[winner] = wc + 1;
            }
        }

        var durationSum = 0.0;
        foreach (var st in series.SeriesTournaments)
        {
            var th = hands.Where(h => h.SeriesTournamentId == st.Id).ToList();
            if (th.Count == 0)
                continue;
            var times = th
                .Select(h => DateTimeOffset.TryParse(h.HandTimeIso, out var dto) ? dto : (DateTimeOffset?)null)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .ToList();
            if (times.Count >= 2)
                durationSum += (times.Max() - times.Min()).TotalSeconds;
        }

        series.StatsTournamentCount = tournamentCount;
        series.StatsWinsByPlayerJson = JsonSerializer.Serialize(wins);
        series.StatsAvgHandsPerTournament = totalHands / (double)tournamentCount;
        series.StatsAvgPromptsPerHand = totalPrompts / (double)totalHands;
        series.StatsAvgPotPerHand = totalPot / totalHands;
        series.StatsAvgTournamentDurationSeconds = tournamentCount > 0 ? durationSum / tournamentCount : 0;

        await db.SaveChangesAsync(cancellationToken);
    }

    public static string FormatStatsForDisplay(TournamentSeries s)
    {
        Dictionary<string, int>? wins = null;
        try
        {
            wins = JsonSerializer.Deserialize<Dictionary<string, int>>(s.StatsWinsByPlayerJson);
        }
        catch
        {
        }

        wins ??= new Dictionary<string, int>();
        var winsLine = wins.Count == 0
            ? "Wins: —"
            : "Wins: " + string.Join(", ", wins.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));

        return
            $"Tournaments: {s.StatsTournamentCount}\n" +
            winsLine + "\n" +
            $"Avg hands / tournament: {s.StatsAvgHandsPerTournament:0.##}\n" +
            $"Avg prompts / hand: {s.StatsAvgPromptsPerHand:0.##}\n" +
            $"Avg pot / hand: {s.StatsAvgPotPerHand:0.##}\n" +
            $"Avg tournament duration: {s.StatsAvgTournamentDurationSeconds:0.##} s";
    }
}
