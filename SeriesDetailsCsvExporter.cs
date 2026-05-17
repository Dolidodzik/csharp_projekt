using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public static class SeriesDetailsCsvExporter
{
    private const int MaxPlayers = 6;

    public static async Task<string> BuildCsvAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var stMap = await db.SeriesTournaments
            .AsNoTracking()
            .Where(s => s.TournamentSeriesId == seriesId)
            .ToDictionaryAsync(s => s.Id, s => s.TournamentIndex, cancellationToken);

        var hands = await db.SavedHands
            .Include(h => h.HandPlayers).ThenInclude(p => p.LlmPersonality)
            .Include(h => h.HandPlayers).ThenInclude(p => p.OpenAiPreset)
            .Where(h => h.TournamentSeriesId == seriesId)
            .ToListAsync(cancellationToken);

        var ordered = hands
            .OrderBy(h => h.SeriesTournamentId ?? 0)
            .ThenBy(h => DateTimeOffset.TryParse(h.HandTimeIso, out var dto) ? dto : DateTimeOffset.MinValue)
            .ThenBy(h => h.Id)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader());
        foreach (var h in ordered)
        {
            var tid = 0;
            if (h.SeriesTournamentId is { } sid && stMap.TryGetValue(sid, out var idx))
                tid = idx + 1;

            var roster = ReplayJsonUtil.RosterNamesFromReplayJson(h.HandHistoryJson);
            var before = ReplayJsonUtil.StartStacksFromReplayJson(h.HandHistoryJson);
            var after = ReplayJsonUtil.FinalStacksFromReplayJson(h.HandHistoryJson);
            var afterOrder = ReplayJsonUtil.FinalStacksInOrderFromReplayJson(h.HandHistoryJson);
            var winners = ReplayJsonUtil.WinnersFromReplayJson(h.HandHistoryJson);
            var winnerName = winners.Count > 0 ? winners[0] : "";
            var handWinner = WinnerSlot(roster, winnerName);

            var byName = h.HandPlayers.ToDictionary(p => p.PlayerName, p => p, StringComparer.Ordinal);

            var row = new List<string>
            {
                C(h.HandTimeIso),
                C(tid.ToString(CultureInfo.InvariantCulture))
            };

            for (var i = 0; i < MaxPlayers; i++)
            {
                if (i < roster.Count && byName.TryGetValue(roster[i], out var hp))
                {
                    row.Add(C(hp.LlmPersonality?.Name));
                    row.Add(C(hp.OpenAiPreset?.Name));
                }
                else
                {
                    row.Add("");
                    row.Add("");
                }
            }

            for (var i = 0; i < MaxPlayers; i++)
            {
                var name = i < roster.Count ? roster[i] : "";
                var b = string.IsNullOrEmpty(name) ? 0 : before.GetValueOrDefault(name);
                var a = i < afterOrder.Count ? afterOrder[i] : (string.IsNullOrEmpty(name) ? 0 : after.GetValueOrDefault(name));
                row.Add(C(b.ToString(CultureInfo.InvariantCulture)));
                row.Add(C(a.ToString(CultureInfo.InvariantCulture)));
            }

            row.Add(C(handWinner));
            sb.AppendLine(string.Join(",", row));
        }

        return sb.ToString();
    }

    private static string WinnerSlot(IReadOnlyList<string> roster, string winnerName)
    {
        if (string.IsNullOrEmpty(winnerName) || roster.Count == 0)
            return "";
        for (var i = 0; i < roster.Count; i++)
        {
            if (string.Equals(roster[i], winnerName, StringComparison.Ordinal))
                return $"player{i + 1}";
        }

        return "";
    }

    private static string BuildHeader()
    {
        var parts = new List<string> { "hand_datetime", "tournament_id" };
        for (var i = 1; i <= MaxPlayers; i++)
        {
            parts.Add($"player_{i}_personality_name");
            parts.Add($"player_{i}_openai_preset_name");
        }

        for (var i = 1; i <= MaxPlayers; i++)
        {
            parts.Add($"player{i}_chips_before_hand");
            parts.Add($"player{i}_chips_after_hand");
        }

        parts.Add("hand_winner");
        return string.Join(",", parts);
    }

    private static string C(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        if (s.Contains('"'))
            s = s.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s}\"";
        return s;
    }
}
