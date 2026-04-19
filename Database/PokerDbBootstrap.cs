using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public static class PokerDbBootstrap
{
    private static DbContextOptions<PokerDbContext>? _options;

    public static void EnsureInitialized()
    {
        if (_options != null)
            return;

        var path = ResolveSqlitePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _options = new DbContextOptionsBuilder<PokerDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        using var db = new PokerDbContext(_options);
        db.Database.EnsureCreated();
        ApplyHandTablesIfMissing(db);
    }

    private static void ApplyHandTablesIfMissing(PokerDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS saved_hand (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hand_name TEXT NOT NULL DEFAULT '',
                hand_history_json TEXT NOT NULL,
                hand_time TEXT NOT NULL
            );
            """);
        try
        {
            db.Database.ExecuteSqlRaw("""
                ALTER TABLE saved_hand ADD COLUMN hand_name TEXT NOT NULL DEFAULT '';
                """);
        }
        catch
        {
        }
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS hand_player (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hand_id INTEGER NOT NULL REFERENCES saved_hand(id) ON DELETE CASCADE,
                player_name TEXT NOT NULL,
                player_type TEXT NOT NULL,
                llm_personality INTEGER NULL REFERENCES llm_agent_personalities(id) ON DELETE SET NULL
            );
            """);
    }

    public static PokerDbContext CreateContext()
    {
        if (_options == null)
            EnsureInitialized();
        return new PokerDbContext(_options!);
    }

    private static string ResolveSqlitePath()
    {
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
        if (File.Exists(Path.Combine(candidate, "PokerApp.csproj")))
            return Path.Combine(candidate, "Database", "llm_personalities.sqlite");
        return Path.Combine(exeDir, "Database", "llm_personalities.sqlite");
    }
}
