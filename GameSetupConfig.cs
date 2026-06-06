using System.Linq;

namespace PokerApp;

/// <summary>typ bota wybierany w combo boxie menu — mapuje się na klasę gracza przy starcie turnieju.</summary>
public enum BotType
{
    RandomBotPlayer,
    LlmBotPlayer
}

/// <summary>snapshot osobowości z bazy — record zamiast encji EF, żeby nie ciągnąć DbContext do stołu.</summary>
/// <param name="Id">null gdy brak przypisanej osobowości.</param>
/// <param name="Name">krótka etykieta w UI.</param>
/// <param name="Description">tekst wstrzykiwany do system promptu LLM.</param>
public sealed record LlmPersonalitySnapshot(int? Id, string Name, string Description);

/// <summary>dane presetu API skopiowane z bazy na czas gry — klucz jest w pamięci tylko podczas sesji.</summary>
public sealed record OpenAiPresetSnapshot(int Id, string Name, string ApiUrl, string ApiKey, string ModelName);

/// <summary>jeden wiersz konfiguracji bota z ekranu setupu.</summary>
public sealed record BotSetup(
    string Name,
    BotType Type,
    LlmPersonalitySnapshot? LlmPersonality = null,
    OpenAiPresetSnapshot? OpenAiPreset = null);

/// <summary>
/// immutable opis stołu — przenosi się z menu do <see cref="MainWindow"/> bez globalnego stanu.
/// </summary>
/// <remarks>
/// SpectatorSeriesMode=true wyłącza człowieka i włącza rosnące blindy w <see cref="TournamentSession"/>.
/// SeriesTournamentNumber/Total to tylko tekst na pasku postępu serii.
/// </remarks>
/// <param name="BuyIn">startowy stack każdego gracza.</param>
/// <param name="SmallBlind">bazowa mała ciemna (big = 2× small).</param>
/// <param name="Bots">1–6 botów, kolejność = kolejność miejsc.</param>
/// <param name="LlmTemperature">0 = deterministyczne odpowiedzi modelu.</param>
/// <param name="SpectatorSeriesMode">tryb „seria turniejów (boty)”.</param>
/// <param name="SeriesTournamentNumber">który turniej w serii (1-based), null poza serią.</param>
/// <param name="SeriesTournamentTotal">ile turniejów zaplanowano, null poza serią.</param>
/// <seealso cref="MainWindowViewModel"/>
public sealed record GameSetupConfig(
    int BuyIn,
    int SmallBlind,
    IReadOnlyList<BotSetup> Bots,
    double LlmTemperature = 0,
    bool SpectatorSeriesMode = false,
    int? SeriesTournamentNumber = null,
    int? SeriesTournamentTotal = null)
{
    /// <value>wyliczane — silnik i prompty używają big blinda, nie small.</value>
    public int BigBlind => SmallBlind * 2;

    /// <value>true gdy choć jeden bot to LLM — menu może ostrzec o braku presetu.</value>
    public bool UsesLlm => Bots.Any(b => b.Type == BotType.LlmBotPlayer);

    /// <summary>domyślny stół do testów — trzech botów losowych, bez człowieka w nazwach botów.</summary>
    /// <returns>konfiguracja z <see cref="GameConstants"/>.</returns>
    public static GameSetupConfig CreateDefault() =>
        new(
            GameConstants.DefaultBuyIn,
            GameConstants.DefaultSmallBlind,
            [
                new BotSetup("Bot A", BotType.RandomBotPlayer),
                new BotSetup("Bot B", BotType.RandomBotPlayer),
                new BotSetup("Bot C", BotType.RandomBotPlayer),
            ],
            LlmTemperature: 0);
}
