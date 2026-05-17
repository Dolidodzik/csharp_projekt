using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public static class TournamentSeriesSetupPreferencesStore
{
    public const int SingletonRowId = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task<TournamentSeriesSetupOptionsPayload?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var row = await db.TournamentSeriesSetupPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == SingletonRowId, cancellationToken);
        if (row is null || string.IsNullOrWhiteSpace(row.OptionsJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TournamentSeriesSetupOptionsPayload>(row.OptionsJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static async Task SaveAsync(TournamentSeriesSetupOptionsPayload payload, CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        var row = await db.TournamentSeriesSetupPreferences.FindAsync(new object[] { SingletonRowId }, cancellationToken);
        if (row is null)
        {
            db.TournamentSeriesSetupPreferences.Add(new TournamentSeriesSetupPreferences
            {
                Id = SingletonRowId,
                OptionsJson = json,
                UpdatedAt = now
            });
        }
        else
        {
            row.OptionsJson = json;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var row = await db.TournamentSeriesSetupPreferences.FindAsync(new object[] { SingletonRowId }, cancellationToken);
        if (row is not null)
        {
            db.TournamentSeriesSetupPreferences.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
