using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Players;

namespace PokerApp;

public interface IGameUi
{
    CancellationToken GameCancellation { get; }

    int CurrentHandBigBlind { get; }

    void RunOnUiThread(Action action);

    void AppendHandHistory(object jsonSerializable);

    void BeginHandHistory(int handNumber);

    void RecordPublicAction(string playerName, string round, PlayerAction action, int moneyToCall, int moneyLeft, string? promptBeforeAction = null, string? thoughtBeforeAction = null);

    void SetStatus(string message);

    void SetCommunityCards(IReadOnlyList<Card> cards);

    void SetRoundLabel(string label);

    void SetPot(int pot);

    void SetPlayerStack(string playerName, int chips);

    void SetCurrentTurn(string? playerName);

    void ClearLastActionsForNewRound();

    void SetHoleCards(string playerName, Card? card1, Card? card2, bool maskOpponentHoles);

    void RevealShowdown(IReadOnlyDictionary<string, IReadOnlyList<Card>> holeCardsByPlayer);

    void RevealAllHoleCards();
}
