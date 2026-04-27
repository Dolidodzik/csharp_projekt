using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Players;

namespace PokerApp;

public static class LlmPrompts
{
    private const string BaseSystemPrompt =
        "You are a Texas Hold'em decision engine. IF PERSONALITY IS PROVIDED, MAKE IT REFLECT IN YOUR DECISIONS, AND IN YOUR REASONING. Answer briefly, then output the action code as the LAST line.";

    public static string BuildSystemPrompt(LlmPersonalitySnapshot? personality)
    {
        if (personality is null)
            return BaseSystemPrompt;
        var hasName = !string.IsNullOrWhiteSpace(personality.Name);
        var hasDesc = !string.IsNullOrWhiteSpace(personality.Description);
        if (!hasName && !hasDesc)
            return BaseSystemPrompt;
        var lines = new List<string> { BaseSystemPrompt, "", "YOUR PLAYING PERSONA:" };
        if (hasName)
            lines.Add($"Name: {personality.Name}");
        if (hasDesc)
            lines.Add(personality.Description);
        return string.Join('\n', lines);
    }

    public static string BuildUserPrompt(
        IGetTurnContext context,
        int bigBlind,
        string heroName,
        IReadOnlyList<Card> ownCards,
        IReadOnlyList<Card> boardCards,
        IReadOnlyDictionary<string, IReadOnlyList<string>> handHistoryByStreet,
        string possibleActions)
    {
        var street = context.RoundType.ToString().Equals("PreFlop", StringComparison.OrdinalIgnoreCase)
            ? "preflop"
            : "postflop";
        var lexicon = UniversalLexicon + "\n" + (street == "preflop" ? PreflopLexicon : PostflopLexicon);
        var questions = UniversalQuestions + "\n" + (street == "preflop" ? PreflopQuestions : PostflopQuestions);
        var factsSection = street == "preflop"
            ? string.Empty
            : $"\n\nFACTS:\n{LlmGameStateFacts.FormatFactsBlock(context, ownCards, boardCards)}";
        var opponents = LlmGameStateFacts.CountOpponentsNotIncludingHero(context);

        return $"""
TASK: Make ONE decision in your last line of answer, choose exact code from the possible actions below. Be brief and concise.
DO NOT WRITE FULL SENTENCES. DO WRITE JUST SHORT ANSWERS.
IF PERSONALITY IS PROVIDED, MAKE IT REFLECT IN YOUR DECISIONS, AND IN YOUR REASONING.

LEXICON AND RULES:
{lexicon}

GAME STATE:
Street: {street}
opponents in the game: {opponents}
Your cards:
{FormatOwnCards(ownCards)}
Community board:
{FormatCommunityBoard(boardCards)}
Pot: {context.CurrentPot}
Your stack: {context.MoneyLeft}
To call: {context.MoneyToCall}
Big blind: {bigBlind}
Min raise: {context.MinRaise}
action history:
{FormatStreetHistory(handHistoryByStreet, heroName)}{factsSection}

QUESTIONS:
{questions}

Possible actions:
{possibleActions}

OUTPUT FORMAT:

<ANSWERS TO QUESTIONS (just answer, dont re-write questions)>

General reasoning, thoughts:
- ...
- ...
- ...

Decision: <action code>
""";
    }

    private static string FormatOwnCards(IReadOnlyList<Card> cards)
    {
        if (cards.Count == 0)
            return "- none";
        return string.Join('\n', cards.Select(c => $"- {c.Type} of {c.Suit}s"));
    }

    private static string FormatCommunityBoard(IReadOnlyList<Card> boardCards) =>
        boardCards.Count == 0 ? "- none" : FormatOwnCards(boardCards);

    private static string FormatNameForLlm(string name) =>
        string.IsNullOrEmpty(name) ? name : name.Replace('_', ' ');

    private const string UniversalLexicon = """
- each player has 2 cards on hand
- preflop betting when you only see your own cards, and none of the community cards
- flop betting round is when 3 cards are shown
- turn betting round is when 4 cards are shown
- river betting round is when 5 cards are shown
- remember your oponents can try to trick you
- pocket pair is when you have two cards of the same rank on your hand
- checking is usually sign of weakness, unless they are trapping you or doing check-raise
- betting small (below 25% pot) is like checking
- betting big (above 75% pot) is display of strength, especially if re-raising previous bet
- bet size relative to pot is important. For example if they bet 50 or 100, into pot of 1000 or 2000, it's almost free to call.
- if they bet 500 or 1000 into pot of 100 or 200, that is huge, and calling is much more expensive.
- betting huge yourself relatively to pot (more than 200% of the pot) is stupid - only do so if you are targeting a hand that you are sure will call that, or you think that size is needed to fold them out.
""";

    private const string PreflopLexicon = """
- the more players are dealt cards preflop, the less hands you should play preflop (only good ones), for example if only one oponent plays, you should play more hands than if three oponents play. If more oponents play better hands, rest you can just instantly fold.
""";

    private const string PostflopLexicon = """
- after betting on the river is over, the only player who hasnt folded win. If more than one didnt fold, showdown happens and better 5 card combination wins.
- with straights or flushes be careful: even if you hit, can you be beaten by a better straight, flush, or full house?
- if board is paired, it means 2 cards of the same rank lie on the board - for example two queens. Only then can fullhouses happen, so straights and flushes become vulnerable.
- you can have 3 of a kind even when the board is not paired if you have a pocket pair
- if for example board is paired and you have just a pair as "your best hand now", it means you have nothing, you just have what everyone else has.
""";

    private const string UniversalQuestions = """
1. how big last bet is relatively to pot?
2. is oponent likely bluffing? does their story make sense?
3. what is the pot size relative to your stack and stacks of oponents?
4. What oponents might have, and how likely - based on their actions on each betting round? For each scenario what are they outs/situation?
5. can we bluff here? if so what hands are we targetting?
6. do we likely have best hand? can we extract value?
""";

    private const string PreflopQuestions = """
0.1 - how good is your hands absolute value preflop?
""";

    private const string PostflopQuestions = """
0.1 - is the board paired?
0.2 - what opponents acted like so far? Did they show strength or weakness?
0.3 - are straights or flushes possible?
0.4 - What are you drawing to?
""";

    private static string FormatStreetHistory(IReadOnlyDictionary<string, IReadOnlyList<string>> byStreet, string heroName)
    {
        if (byStreet.Count == 0)
            return "none";

        var ordered = new[] { "Pre-Flop", "Flop", "Turn", "River" };
        var aliases = BuildPlayerAliases(byStreet, heroName);
        var lines = new List<string>();
        foreach (var street in ordered)
        {
            if (!byStreet.TryGetValue(street, out var actions) || actions.Count == 0)
                continue;
            lines.Add($"{street}:");
            foreach (var action in actions)
                lines.Add(FormatHistoryActionLine(action, aliases));
        }
        return lines.Count == 0 ? "none" : string.Join('\n', lines);
    }

    private static string FormatHistoryActionLine(string line, IReadOnlyDictionary<string, string> aliases)
    {
        var trimmed = line.TrimStart();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
            return $"  {line}";
        var player = trimmed[..colon].TrimEnd();
        var rest = trimmed[colon..];
        if (!aliases.TryGetValue(player, out var alias))
            alias = FormatNameForLlm(player);
        return $"  {alias}{rest}";
    }

    private static Dictionary<string, string> BuildPlayerAliases(IReadOnlyDictionary<string, IReadOnlyList<string>> byStreet, string heroName)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var ordered = new[] { "Pre-Flop", "Flop", "Turn", "River" };
        var opponentIndex = 1;
        foreach (var street in ordered)
        {
            if (!byStreet.TryGetValue(street, out var actions))
                continue;
            foreach (var action in actions)
            {
                var trimmed = action.TrimStart();
                var colon = trimmed.IndexOf(':');
                if (colon <= 0)
                    continue;
                var player = trimmed[..colon].TrimEnd();
                if (aliases.ContainsKey(player))
                    continue;
                if (string.Equals(player, heroName, StringComparison.Ordinal))
                    aliases[player] = "US";
                else
                    aliases[player] = $"oponent {opponentIndex++}";
            }
        }
        return aliases;
    }
}
