using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public static class PokerDbBootstrap
{
    private const string SchemaVersion = "3";

    private static DbContextOptions<PokerDbContext>? _options;

    public static void EnsureInitialized()
    {
        if (_options != null)
            return;

        var path = ResolveSqlitePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var verPath = path + ".ver";
        var matches = File.Exists(verPath) && File.ReadAllText(verPath).Trim() == SchemaVersion;
        if (!matches)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }

            File.WriteAllText(verPath, SchemaVersion);
        }

        _options = new DbContextOptionsBuilder<PokerDbContext>()
            .UseSqlite($"Data Source={path};Mode=ReadWriteCreate;Cache=Shared")
            .Options;

        using var db = new PokerDbContext(_options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS tournament_series_setup_prefs (
                id INTEGER NOT NULL PRIMARY KEY,
                options_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
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
        var env = Environment.GetEnvironmentVariable("POKERAPP_DB");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env.Trim());

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokerApp");
        return Path.Combine(root, "poker_app.sqlite");
    }
}
