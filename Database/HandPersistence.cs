using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public static class HandPersistence
{
    public static async Task SaveAsync(
        string handName,
        string handHistoryJson,
        IReadOnlyList<(string Name, string PlayerType, int? LlmPersonalityId)> seats,
        CancellationToken cancellationToken = default)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var entity = new SavedHand
        {
            HandName = handName,
            HandHistoryJson = handHistoryJson,
            HandTimeIso = DateTimeOffset.UtcNow.ToString("O")
        };
        db.SavedHands.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var (name, playerType, pid) in seats)
        {
            db.HandPlayers.Add(new HandPlayer
            {
                HandId = entity.Id,
                PlayerName = name,
                PlayerType = playerType,
                LlmPersonalityId = pid
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
