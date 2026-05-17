namespace PokerApp;

public static class TournamentBlindSchedule
{
    public static int SmallBlindForHand(int baseSmallBlind, int handNumberOneBased, bool escalating)
    {
        if (!escalating || handNumberOneBased < 1)
            return baseSmallBlind;

        var tier = (handNumberOneBased - 1) / 10;
        var mult = Math.Min(tier + 1, 4);
        return baseSmallBlind * mult;
    }
}
