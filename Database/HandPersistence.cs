using Microsoft.EntityFrameworkCore;

namespace PokerApp;

/// <summary>
/// zapis i odczyt rozdań — warstwa między UI a <see cref="PokerDbContext"/>.
/// </summary>
/// <remarks>
/// max_pot liczymy przy zapisie z JSON (<see cref="ReplayJsonUtil.MaxPotFromReplayJson"/>),
/// żeby lista powtórek mogła filtrować małe pule bez ładowania całej historii.
/// </remarks>
/// <seealso cref="MainMenuWindow"/>
public static class HandPersistence
{
    /// <summary>zapis ręki z gry z człowiekiem — bez powiązania z serią turniejów.</summary>
    /// <param name="handName">nazwa podana przez użytkownika w overlayu.</param>
    /// <param name="handHistoryJson">wynik <see cref="MainWindowViewModel.BuildHandReplayJson"/>.</param>
    /// <param name="seats">skład stołu — typ gracza + FK do osobowości/presetu LLM.</param>
    /// <param name="cancellationToken">anulowanie przy zamykaniu okna.</param>
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

    /// <summary>
    /// auto-zapis każdej ręki w serii botów — nazwa generowana (Turniej X, Rozdanie Y).
    /// </summary>
    /// <param name="handName">np. „Turniej 2, Rozdanie 14”.</param>
    /// <param name="handHistoryJson">JSON z <see cref="MainWindowViewModel.BuildHandReplayJson"/>.</param>
    /// <param name="seats">skład stołu w tym rozdaniu.</param>
    /// <param name="tournamentSeriesId">FK do tournament_series.</param>
    /// <param name="seriesTournamentId">FK do series_tournament (konkretny turniej w serii).</param>
    /// <param name="cancellationToken">anulowanie serii.</param>
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

    /// <summary>pełny JSON ładowany dopiero po „Otwórz” w powtórkach — lista trzyma tylko metadane.</summary>
    /// <param name="handId">id z saved_hand.</param>
    /// <param name="cancellationToken">anulowanie ładowania.</param>
    /// <returns>pusty string gdy rekord nie istnieje.</returns>
    public static async Task<string> LoadHandHistoryJsonAsync(int handId, CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var json = await db.SavedHands
            .AsNoTracking()
            .Where(h => h.Id == handId)
            .Select(h => h.HandHistoryJson)
            .FirstOrDefaultAsync(cancellationToken);
        return json ?? string.Empty;
    }

    /// <summary>
    /// sprzątanie po anulowaniu serii w trakcie turnieju — usuwa ręce i wpis series_tournament.
    /// </summary>
    /// <param name="seriesTournamentId">id przerwanego turnieju.</param>
    /// <param name="cancellationToken">token anulowania.</param>
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

    /// <summary>usuwa całą serię kaskadowo (ręce, turnieje, nagłówek serii).</summary>
    /// <param name="seriesId">id tournament_series.</param>
    /// <param name="cancellationToken">token anulowania.</param>
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
