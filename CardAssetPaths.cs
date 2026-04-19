using TexasHoldem.Logic.Cards;

namespace PokerApp;

public static class CardAssetPaths
{
    private static string RankFolder(CardType t) => t switch
    {
        CardType.Two => "2",
        CardType.Three => "3",
        CardType.Four => "4",
        CardType.Five => "5",
        CardType.Six => "6",
        CardType.Seven => "7",
        CardType.Eight => "8",
        CardType.Nine => "9",
        CardType.Ten => "10",
        CardType.Jack => "jack",
        CardType.Queen => "queen",
        CardType.King => "king",
        CardType.Ace => "ace",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null)
    };

    private static string SuitFolder(CardSuit s) => s switch
    {
        CardSuit.Club => "clubs",
        CardSuit.Diamond => "diamonds",
        CardSuit.Heart => "hearts",
        CardSuit.Spade => "spades",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
    };

    public static string RelativeSvgPath(Card card) =>
        Path.Combine("assets", "svg-cards", $"{RankFolder(card.Type)}_of_{SuitFolder(card.Suit)}.svg");

    public static string AbsoluteSvgPath(Card card) =>
        Path.Combine(AppContext.BaseDirectory, RelativeSvgPath(card));
}
