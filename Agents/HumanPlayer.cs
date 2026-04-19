using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Players;

namespace PokerApp;

public sealed class HumanPlayer : BasePlayer
{
    private readonly IGameUi _ui;
    private readonly Func<IGetTurnContext, PlayerAction> _waitForHuman;

    public HumanPlayer(IGameUi ui, Func<IGetTurnContext, PlayerAction> waitForHuman)
    {
        _ui = ui;
        _waitForHuman = waitForHuman;
    }

    public override string Name => GameConstants.HumanPlayerName;

    public override int BuyIn => -1;

    public override PlayerAction PostingBlind(IPostingBlindContext context)
    {
        _ui.SetPlayerStack(Name, context.CurrentStackSize);
        _ui.SetPot(context.CurrentPot);
        return context.BlindAction;
    }

    public override PlayerAction GetTurn(IGetTurnContext context)
    {
        _ui.SetCurrentTurn(Name);
        var action = _waitForHuman(context);
        var contributed = EstimateContribution(context, action);
        _ui.SetPlayerStack(Name, context.MoneyLeft - contributed);
        _ui.SetPot(context.CurrentPot + contributed);
        _ui.RecordPublicAction(Name, context.RoundType.ToString(), action, context.MoneyToCall, context.MoneyLeft);
        _ui.AppendHandHistory(new
        {
            ev = "action",
            player = Name,
            round = context.RoundType.ToString(),
            action = action.ToString(),
            moneyToCall = context.MoneyToCall,
            pot = context.CurrentPot,
            stack = context.MoneyLeft,
            prompt_before_action = (string?)null,
            thought_before_action = (string?)null
        });
        return action;
    }

    public override void StartHand(IStartHandContext context)
    {
        base.StartHand(context);
        _ui.SetCurrentTurn(null);
        _ui.BeginHandHistory(context.HandNumber);
        _ui.SetPot(0);
        _ui.RunOnUiThread(() =>
        {
            _ui.SetPlayerStack(Name, context.MoneyLeft);
            _ui.SetHoleCards(Name, FirstCard, SecondCard, maskOpponentHoles: false);
        });
        _ui.AppendHandHistory(new
        {
            ev = "start_hand",
            player = Name,
            hand = context.HandNumber,
            stack = context.MoneyLeft,
            cards = new[]
            {
                new { rank = FirstCard.Type.ToString(), suit = FirstCard.Suit.ToString() },
                new { rank = SecondCard.Type.ToString(), suit = SecondCard.Suit.ToString() }
            }
        });
    }

    public override void StartRound(IStartRoundContext context)
    {
        base.StartRound(context);
        var communityCards = SnapshotCards(context.CommunityCards);
        _ui.AppendHandHistory(new
        {
            ev = "start_round",
            player = Name,
            round = context.RoundType.ToString(),
            community_cards = communityCards.Select(CardJson).ToArray()
        });
        _ui.RunOnUiThread(() =>
        {
            _ui.SetRoundLabel(context.RoundType.ToString());
            _ui.SetCommunityCards(communityCards);
            _ui.SetPot(context.CurrentPot);
            _ui.SetPlayerStack(Name, context.MoneyLeft);
        });
    }

    public override void EndRound(IEndRoundContext context)
    {
        _ui.ClearLastActionsForNewRound();
        _ui.AppendHandHistory(new
        {
            ev = "end_round",
            player = Name,
            actions = context.RoundActions.Select(a => new { a.PlayerName, action = a.Action.ToString() }).ToArray()
        });
        base.EndRound(context);
    }

    public override void EndHand(IEndHandContext context)
    {
        if (context.ShowdownCards.Count > 0)
        {
            var revealed = context.ShowdownCards.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<Card>)kv.Value.ToList());
            _ui.RunOnUiThread(() => _ui.RevealShowdown(revealed));
            _ui.AppendHandHistory(new
            {
                ev = "showdown",
                cards = context.ShowdownCards.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(c => new { rank = c.Type.ToString(), suit = c.Suit.ToString() }).ToArray())
            });
        }

        _ui.RevealAllHoleCards();
        _ui.SetCurrentTurn(null);
        base.EndHand(context);
    }

    private static int EstimateContribution(IGetTurnContext context, PlayerAction action)
    {
        return action.Type switch
        {
            PlayerActionType.Fold => 0,
            PlayerActionType.CheckCall => Math.Min(context.MoneyLeft, context.MoneyToCall),
            PlayerActionType.Raise => Math.Min(context.MoneyLeft, context.MoneyToCall + Math.Max(0, action.Money)),
            _ => 0
        };
    }

    private static object CardJson(Card c) => new { rank = c.Type.ToString(), suit = c.Suit.ToString() };

    private static IReadOnlyList<Card> SnapshotCards(IEnumerable<Card> source)
    {
        var cards = new List<Card>(5);
        foreach (var card in source)
            cards.Add(card);
        return cards;
    }
}
