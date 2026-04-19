using System.Text;
using TexasHoldem.Logic;
using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Helpers;
using TexasHoldem.Logic.Players;

namespace PokerApp;

public static class LlmGameStateFacts
{
    private static readonly IHandEvaluator Evaluator = new HandEvaluator();

    public static int CountOpponentsNotIncludingHero(IGetTurnContext context) =>
        Math.Max(0, context.MainPot.ActivePlayer.Count - 1);

    private static readonly CardSuit[] SuitOrder =
    {
        CardSuit.Club, CardSuit.Diamond, CardSuit.Heart, CardSuit.Spade
    };

    public static string FormatFactsBlock(IGetTurnContext context, IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        var sb = new StringBuilder();

        var suited = hole.Count == 2 && hole[0].Suit == hole[1].Suit;
        sb.AppendLine($"is hand suited: {(suited ? "yes" : "no")}");

        sb.AppendLine("your card suits:");
        AppendSuitCounts(sb, hole, "  ");
        sb.AppendLine("board card suits:");
        AppendSuitCounts(sb, board, "  ");

        var heroKnown = new List<Card>(hole.Count + board.Count);
        heroKnown.AddRange(hole);
        heroKnown.AddRange(board);
        var heroSet = new HashSet<Card>(heroKnown);
        var unseen = Deck.AllCards.Where(c => !heroSet.Contains(c)).ToList();

        AppendOneCardDrawStats(sb, heroKnown, unseen, board.Count);

        sb.AppendLine(FormatBestMadeHandLine(hole, board));

        return sb.ToString().TrimEnd();
    }

    private static void AppendSuitCounts(StringBuilder sb, IReadOnlyList<Card> cards, string indent)
    {
        foreach (var suit in SuitOrder)
        {
            var n = cards.Count(c => c.Suit == suit);
            sb.AppendLine($"{indent}{SuitLabel(suit)}: {n}");
        }
    }

    private static string SuitLabel(CardSuit s) =>
        s switch
        {
            CardSuit.Club => "clubs",
            CardSuit.Diamond => "diamonds",
            CardSuit.Heart => "hearts",
            CardSuit.Spade => "spades",
            _ => s.ToString().ToLowerInvariant()
        };

    private static void AppendOneCardDrawStats(StringBuilder sb, List<Card> heroKnown, List<Card> unseen, int boardCount)
    {
        if (boardCount is not (3 or 4))
        {
            var why = boardCount == 0
                ? "preflop — next street adds three board cards, not one"
                : boardCount == 5
                    ? "river — no further board card"
                    : "board size not flop/turn";
            sb.AppendLine($"your chance of flush on next card: N/A ({why})");
            sb.AppendLine($"your chance of straight on next card: N/A ({why})");
            return;
        }

        var denom = unseen.Count;
        if (denom <= 0)
        {
            sb.AppendLine("your chance of flush on next card: N/A");
            sb.AppendLine("your chance of straight on next card: N/A");
            return;
        }

        var flushOuts = 0;
        var flushMade = false;
        foreach (var suit in SuitOrder)
        {
            var knownInSuit = heroKnown.Count(c => c.Suit == suit);
            if (knownInSuit >= 5)
                flushMade = true;
            else if (knownInSuit == 4)
                flushOuts += unseen.Count(c => c.Suit == suit);
        }

        if (flushMade)
            sb.AppendLine("your chance of flush on next card: 0% (you already have a flush)");
        else
            sb.AppendLine($"your chance of flush on next card: {Percent(flushOuts, denom)}%");

        BestHand? currentBest = null;
        if (heroKnown.Count >= 5)
            currentBest = Evaluator.GetBestHand(heroKnown);

        if (currentBest is { RankType: HandRankType.Straight or HandRankType.StraightFlush })
        {
            sb.AppendLine("your chance of straight on next card: 0% (you already have a straight)");
            return;
        }

        var straightOuts = 0;
        foreach (var c in unseen)
        {
            var withNext = new List<Card>(heroKnown.Count + 1);
            withNext.AddRange(heroKnown);
            withNext.Add(c);
            var best = Evaluator.GetBestHand(withNext);
            if (best.RankType is HandRankType.Straight or HandRankType.StraightFlush)
                straightOuts++;
        }

        sb.AppendLine($"your chance of straight on next card: {Percent(straightOuts, denom)}%");
    }

    private static int Percent(int outs, int denom) =>
        denom <= 0 ? 0 : (int)Math.Round(100.0 * outs / denom);

    private static string FormatBestMadeHandLine(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        var all = new List<Card>(hole.Count + board.Count);
        all.AddRange(hole);
        all.AddRange(board);
        if (all.Count < 5)
        {
            return
                "your best hand now: N/A preflop";
        }

        var best = Evaluator.GetBestHand(all);
        return $"your best hand now: {best.RankType}";
    }
}
