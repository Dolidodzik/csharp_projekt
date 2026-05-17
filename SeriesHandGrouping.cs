using System.Text.RegularExpressions;

namespace PokerApp;

public static class SeriesHandGrouping
{
    private static readonly Regex HandNameTournamentRegex = new(
        @"^(?:T(?<t>\d+)\s+H\d+|Turniej\s+(?<t>\d+)\s*,\s*Rozdanie\s+\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record TournamentBucket(int TournamentNumber, List<SavedHand> Hands);

    public sealed record SeriesStatsSnapshot(
        int TournamentCount,
        Dictionary<string, int> TournamentWinsByPlayer,
        double AvgHandsPerTournament,
        double AvgPromptsPerHand,
        double AvgPotPerHand,
        double AvgTournamentDurationSeconds);

    public static bool TryParseTournamentFromHandName(string handName, out int tournamentNumber)
    {
        tournamentNumber = 0;
        var m = HandNameTournamentRegex.Match(handName.Trim());
        if (!m.Success || !int.TryParse(m.Groups["t"].Value, out tournamentNumber))
            return false;
        return tournamentNumber > 0;
    }

    public static int ResolveTournamentNumber(SavedHand hand, IReadOnlyDictionary<int, int>? seriesTournamentIdToIndex)
    {
        if (TryParseTournamentFromHandName(hand.HandName, out var n))
            return n;
        if (hand.SeriesTournamentId is int sid && seriesTournamentIdToIndex?.TryGetValue(sid, out var idx) == true)
            return idx + 1;
        if (hand.SeriesTournamentId is int raw)
            return raw;
        return 0;
    }

    public static IReadOnlyList<TournamentBucket> GroupByTournament(
        IReadOnlyList<SavedHand> hands,
        IReadOnlyDictionary<int, int>? seriesTournamentIdToIndex)
    {
        var ordered = hands
            .OrderBy(h => ResolveTournamentNumber(h, seriesTournamentIdToIndex))
            .ThenBy(h => DateTimeOffset.TryParse(h.HandTimeIso, out var dto) ? dto : DateTimeOffset.MinValue)
            .ThenBy(h => h.Id)
            .ToList();

        var buckets = new List<TournamentBucket>();
        if (ordered.Count == 0)
            return buckets;

        (int TournamentNumber, int SeriesTournamentId) currentKey = default;
        List<SavedHand>? currentHands = null;

        foreach (var h in ordered)
        {
            var num = ResolveTournamentNumber(h, seriesTournamentIdToIndex);
            var stId = h.SeriesTournamentId ?? 0;
            var key = (num, stId);
            if (currentHands is null || key != currentKey)
            {
                if (currentHands is { Count: > 0 })
                    buckets.Add(new TournamentBucket(currentKey.TournamentNumber, currentHands));
                currentKey = key;
                currentHands = new List<SavedHand>();
            }

            currentHands.Add(h);
        }

        if (currentHands is { Count: > 0 })
            buckets.Add(new TournamentBucket(currentKey.TournamentNumber, currentHands));

        return buckets;
    }

    public static string? TournamentWinnerFromHands(IReadOnlyList<SavedHand> handsInTournament)
    {
        if (handsInTournament.Count == 0)
            return null;
        var ordered = handsInTournament
            .OrderBy(h => DateTimeOffset.TryParse(h.HandTimeIso, out var dto) ? dto : DateTimeOffset.MinValue)
            .ThenBy(h => h.Id)
            .ToList();
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ReplayJsonUtil.TournamentFinishedInReplayJson(ordered[i].HandHistoryJson))
            {
                var w = ReplayJsonUtil.TournamentWinnerFromReplayJson(ordered[i].HandHistoryJson);
                if (!string.IsNullOrEmpty(w))
                    return w;
            }
        }

        return ReplayJsonUtil.WinnersFromReplayJson(ordered[^1].HandHistoryJson).FirstOrDefault();
    }

    public static SeriesStatsSnapshot ComputeStats(
        IReadOnlyList<SavedHand> hands,
        IReadOnlyDictionary<int, int>? seriesTournamentIdToIndex)
    {
        if (hands.Count == 0)
        {
            return new SeriesStatsSnapshot(0, new Dictionary<string, int>(StringComparer.Ordinal), 0, 0, 0, 0);
        }

        var buckets = GroupByTournament(hands, seriesTournamentIdToIndex);
        var playedBuckets = buckets.Where(b => b.Hands.Count > 0).ToList();

        var wins = new Dictionary<string, int>(StringComparer.Ordinal);
        var durationSum = 0.0;
        foreach (var bucket in playedBuckets)
        {
            var winner = TournamentWinnerFromHands(bucket.Hands);
            if (!string.IsNullOrEmpty(winner))
            {
                wins.TryGetValue(winner, out var wc);
                wins[winner] = wc + 1;
            }

            var times = bucket.Hands
                .Select(h => DateTimeOffset.TryParse(h.HandTimeIso, out var dto) ? dto : (DateTimeOffset?)null)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .ToList();
            if (times.Count >= 2)
                durationSum += (times.Max() - times.Min()).TotalSeconds;
        }

        var totalHands = hands.Count;
        var totalPrompts = hands.Sum(h => ReplayJsonUtil.CountPromptsFromReplayJson(h.HandHistoryJson));
        var totalPot = hands.Sum(h => (double)h.MaxPot);
        var divisor = playedBuckets.Count > 0 ? playedBuckets.Count : 1;

        return new SeriesStatsSnapshot(
            playedBuckets.Count,
            wins,
            totalHands / (double)divisor,
            totalPrompts / totalHands,
            totalPot / totalHands,
            durationSum / divisor);
    }
}
