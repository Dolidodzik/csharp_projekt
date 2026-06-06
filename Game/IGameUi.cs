using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Players;

namespace PokerApp;

/// <summary>
/// most między silnikiem pokera a UI — gracze nie znają Avalonia, tylko ten kontrakt.
/// dzięki temu ten sam <see cref="LlmBotPlayer"/> działa w grze na żywo i zapisuje historię do powtórki.
/// </summary>
/// <remarks>
/// silnik woła callbacki graczy z wątku puli (Task.Run), więc każda metoda aktualizująca widok
/// powinna iść przez <see cref="RunOnUiThread"/> albo być wywołana z niego.
/// </remarks>
/// <seealso cref="MainWindowViewModel"/>
/// <seealso cref="HumanPlayer"/>
/// <seealso cref="LlmBotPlayer"/>
public interface IGameUi
{
    /// <value>token anulowania gry — boty sprawdzają go w GetTurn, żeby nie wisieć na API po wyjściu z menu.</value>
    CancellationToken GameCancellation { get; }

    /// <value>aktualny big blind tej ręki — potrzebny LLM-owi do promptów (stos wielokrotności BB).</value>
    int CurrentHandBigBlind { get; }

    /// <summary>przekazuje akcję na wątek UI Avalonia — bez tego bindingi się wywalają.</summary>
    /// <param name="action">co ma się wykonać na Dispatcherze.</param>
    void RunOnUiThread(Action action);

    /// <summary>
    /// dopisuje zdarzenie do bufora powtórki (start_hand, action, showdown itd.).
    /// serializacja do JSON następuje później w <see cref="MainWindowViewModel.BuildHandReplayJson"/>.
    /// </summary>
    /// <param name="jsonSerializable">anonimowy obiekt lub DTO — musi przejść przez System.Text.Json.</param>
    void AppendHandHistory(object jsonSerializable);

    /// <summary>resetuje bufor historii na początek nowej ręki.</summary>
    /// <param name="handNumber">numer rozdania w turnieju (1-based).</param>
    void BeginHandHistory(int handNumber);

    /// <summary>
    /// zapisuje publiczną akcję do panelu historii i do JSON.
    /// opcjonalne prompt/thought — tylko boty LLM, reszta zostawia null.
    /// </summary>
    /// <param name="playerName">kto zagrał.</param>
    /// <param name="round">Preflop, Flop, Turn, River.</param>
    /// <param name="action">decyzja z silnika TexasHoldem.</param>
    /// <param name="moneyToCall">ile brakowało do calla w momencie decyzji.</param>
    /// <param name="moneyLeft">stack przed odjęciem wpłaty do puli.</param>
    /// <param name="promptBeforeAction">pełny user prompt wysłany do API (powtórka diagnostyczna).</param>
    /// <param name="thoughtBeforeAction">surowa odpowiedź modelu przed parsowaniem akcji.</param>
    void RecordPublicAction(string playerName, string round, PlayerAction action, int moneyToCall, int moneyLeft, string? promptBeforeAction = null, string? thoughtBeforeAction = null);

    /// <summary>krótki komunikat na pasku statusu stołu.</summary>
    void SetStatus(string message);

    /// <summary>ustawia karty wspólne na boardzie (0–5 kart).</summary>
    void SetCommunityCards(IReadOnlyList<Card> cards);

    /// <summary>etykieta rundy — np. „Flop”, widoczna nad stołem.</summary>
    void SetRoundLabel(string label);

    /// <summary>aktualna pula — gracze aktualizują ją po każdej akcji, bo silnik nie robi tego za nas.</summary>
    void SetPot(int pot);

    /// <summary>stack gracza po ostatniej znanej akcji.</summary>
    /// <param name="playerName">nazwa z <see cref="GameSetupConfig"/> / <see cref="GameConstants.HumanPlayerName"/>.</param>
    /// <param name="chips">żetony, 0 = wyeliminowany.</param>
    void SetPlayerStack(string playerName, int chips);

    /// <summary>kto teraz myśli — null gdy nikt nie gra albo koniec ręki.</summary>
    void SetCurrentTurn(string? playerName);

    /// <summary>czyści „ostatnią akcję” przy miejscach — nowa ulica, nowa etykieta.</summary>
    void ClearLastActionsForNewRound();

    /// <summary>
    /// pokazuje hole cards. maskOpponentHoles=true ukrywa karty przeciwników w grze na żywo;
    /// w powtórce zawsze false, bo tam wszystko widać od początku.
    /// </summary>
    void SetHoleCards(string playerName, Card? card1, Card? card2, bool maskOpponentHoles);

    /// <summary>odkrywa karty tylko uczestników showdownu.</summary>
    void RevealShowdown(IReadOnlyDictionary<string, IReadOnlyList<Card>> holeCardsByPlayer);

    /// <summary>po zakończeniu ręki — pokazuje wszystkie karty (np. gdy ktoś spasował wcześniej).</summary>
    void RevealAllHoleCards();
}
