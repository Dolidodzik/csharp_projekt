using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public static class HandPersistence
{
    public static async Task SaveAsync(
        string handName,
        string handHistoryJson,
        IReadOnlyList<(string Name, string PlayerType, int? LlmPersonalityId, int? OpenAiPresetId)> seats,
        CancellationToken cancellationToken = default)
    {
        var maxPot = ReplayJsonUtil.MaxPotFromReplayJson(handHistoryJson);
        await SaveInternalAsync(
            handName,
            handHistoryJson,
            seats,
            null,
            null,
            maxPot,
            cancellationToken);
    }

    public static async Task SaveSeriesHandAsync(
        string handName,
        string handHistoryJson,
        IReadOnlyList<(string Name, string PlayerType, int? LlmPersonalityId, int? OpenAiPresetId)> seats,
        int tournamentSeriesId,
        int seriesTournamentId,
        CancellationToken cancellationToken = default)
    {
        var maxPot = ReplayJsonUtil.MaxPotFromReplayJson(handHistoryJson);
        await SaveInternalAsync(
            handName,
            handHistoryJson,
            seats,
            tournamentSeriesId,
            seriesTournamentId,
            maxPot,
            cancellationToken);
    }

    private static async Task SaveInternalAsync(
        string handName,
        string handHistoryJson,
        IReadOnlyList<(string Name, string PlayerType, int? LlmPersonalityId, int? OpenAiPresetId)> seats,
        int? tournamentSeriesId,
        int? seriesTournamentId,
        int maxPot,
        CancellationToken cancellationToken)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var entity = new SavedHand
        {
            HandName = handName,
            HandHistoryJson = handHistoryJson,
            HandTimeIso = DateTimeOffset.UtcNow.ToString("O"),
            MaxPot = maxPot,
            TournamentSeriesId = tournamentSeriesId,
            SeriesTournamentId = seriesTournamentId
        };
        db.SavedHands.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var (name, playerType, pid, presetId) in seats)
        {
            db.HandPlayers.Add(new HandPlayer
            {
                HandId = entity.Id,
                PlayerName = name,
                PlayerType = playerType,
                LlmPersonalityId = pid,
                OpenAiPresetId = presetId
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task DiscardSeriesTournamentAsync(int seriesTournamentId, CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var hands = await db.SavedHands.Where(h => h.SeriesTournamentId == seriesTournamentId).ToListAsync(cancellationToken);
        db.SavedHands.RemoveRange(hands);
        var st = await db.SeriesTournaments.FindAsync(new object[] { seriesTournamentId }, cancellationToken);
        if (st is not null)
            db.SeriesTournaments.Remove(st);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task DeleteTournamentSeriesAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var hands = await db.SavedHands.Where(h => h.TournamentSeriesId == seriesId).ToListAsync(cancellationToken);
        db.SavedHands.RemoveRange(hands);
        var sts = await db.SeriesTournaments.Where(s => s.TournamentSeriesId == seriesId).ToListAsync(cancellationToken);
        db.SeriesTournaments.RemoveRange(sts);
        var se = await db.TournamentSeries.FindAsync(new object[] { seriesId }, cancellationToken);
        if (se is not null)
            db.TournamentSeries.Remove(se);
        await db.SaveChangesAsync(cancellationToken);
    }
}
