using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Players;

namespace PokerApp;

public sealed class LlmBotPlayer : BasePlayer
{
    private readonly IGameUi _ui;
    private readonly OpenAiCompatClient _client;
    private readonly LlmPersonalitySnapshot? _personality;
    private readonly int _bigBlind;
    private readonly Dictionary<string, List<string>> _handHistoryByStreet = new(StringComparer.Ordinal);
    private IReadOnlyList<Card> _currentBoard = Array.Empty<Card>();
    private string _currentStreet = "Pre-Flop";

    public LlmBotPlayer(string name, IGameUi ui, OpenAiCompatClient client, int bigBlind, LlmPersonalitySnapshot? personality = null)
    {
        Name = name;
        _ui = ui;
        _client = client;
        _bigBlind = bigBlind;
        _personality = personality;
    }

    public override string Name { get; }
    public override int BuyIn => -1;

    public override PlayerAction PostingBlind(IPostingBlindContext context)
    {
        _ui.SetPlayerStack(Name, context.CurrentStackSize);
        _ui.SetPot(context.CurrentPot);
        return context.BlindAction;
    }

    public override void StartHand(IStartHandContext context)
    {
        base.StartHand(context);
        _handHistoryByStreet.Clear();
        _currentBoard = Array.Empty<Card>();
        _currentStreet = "Pre-Flop";
        _ui.BeginHandHistory(context.HandNumber);
        _ui.SetCurrentTurn(null);
        _ui.SetPot(0);
        _ui.RunOnUiThread(() =>
        {
            _ui.SetPlayerStack(Name, context.MoneyLeft);
            _ui.SetHoleCards(Name, FirstCard, SecondCard, maskOpponentHoles: true);
        });
        _ui.AppendHandHistory(new
        {
            ev = "start_hand",
            player = Name,
            hand = context.HandNumber,
            stack = context.MoneyLeft,
            cards = new[] { CardJson(FirstCard), CardJson(SecondCard) },
            llm_personality_id = _personality?.Id,
            llm_personality_name = string.IsNullOrWhiteSpace(_personality?.Name) ? null : _personality.Name
        });
    }

    public override void StartRound(IStartRoundContext context)
    {
        base.StartRound(context);
        var board = SnapshotCards(context.CommunityCards);
        _currentBoard = board;
        _currentStreet = NormalizeRoundName(context.RoundType.ToString());
        _ui.AppendHandHistory(new
        {
            ev = "start_round",
            player = Name,
            round = context.RoundType.ToString(),
            community_cards = board.Select(CardJson).ToArray()
        });
        _ui.RunOnUiThread(() =>
        {
            _ui.SetRoundLabel(context.RoundType.ToString());
            _ui.SetCommunityCards(board);
            _ui.SetPot(context.CurrentPot);
            _ui.SetPlayerStack(Name, context.MoneyLeft);
        });
    }

    public override PlayerAction GetTurn(IGetTurnContext context)
    {
        _ui.SetCurrentTurn(Name);
        var possible = BuildPossibleActions(context);

        if (possible.HasSingleLegalAction)
            return FinalizeAction(context, possible.SingleLegalAction, null, null);

        var ownCards = new List<Card> { FirstCard, SecondCard };
        var userPrompt = LlmPrompts.BuildUserPrompt(
            context,
            _bigBlind,
            ownCards,
            _currentBoard,
            _handHistoryByStreet.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value),
            possible.PossibleActionsString);

        var systemPrompt = LlmPrompts.BuildSystemPrompt(_personality);
        var promptBefore = systemPrompt + "\n---\n" + userPrompt;

        string? output;
        try
        {
            output = _client.CompleteChatAsync(systemPrompt, userPrompt, _ui.GameCancellation).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsLLmRequestTimeoutOrCanceled(ex))
        {
            var safe = context.CanCheck ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
            return FinalizeAction(context, safe, promptBefore, ex.GetType().Name + ": " + ex.Message);
        }

        var action = ParseActionCode(output, context, possible);
        return FinalizeAction(context, action, promptBefore, output);
    }

    private static bool IsLLmRequestTimeoutOrCanceled(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is TaskCanceledException or OperationCanceledException or TimeoutException)
                return true;
        }

        return false;
    }

    public override void EndRound(IEndRoundContext context)
    {
        _ui.ClearLastActionsForNewRound();
        if (!_handHistoryByStreet.TryGetValue(_currentStreet, out var list))
        {
            list = new List<string>();
            _handHistoryByStreet[_currentStreet] = list;
        }
        foreach (var action in context.RoundActions)
            list.Add($"{action.PlayerName}: {action.Action}");
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
                    kv => kv.Value.Select(CardJson).ToArray())
            });
        }

        _ui.RevealAllHoleCards();
        _ui.SetCurrentTurn(null);
        base.EndHand(context);
    }

    private PlayerAction FinalizeAction(IGetTurnContext context, PlayerAction action, string? promptBefore, string? thoughtBefore)
    {
        var contributed = EstimateContribution(context, action);
        _ui.SetPlayerStack(Name, context.MoneyLeft - contributed);
        _ui.SetPot(context.CurrentPot + contributed);
        _ui.RecordPublicAction(Name, context.RoundType.ToString(), action, context.MoneyToCall, context.MoneyLeft, promptBefore, thoughtBefore);
        _ui.AppendHandHistory(new
        {
            ev = "action",
            player = Name,
            round = context.RoundType.ToString(),
            action = action.ToString(),
            moneyToCall = context.MoneyToCall,
            pot = context.CurrentPot,
            stack = context.MoneyLeft,
            prompt_before_action = promptBefore,
            thought_before_action = thoughtBefore,
            llm_personality_id = _personality?.Id,
            llm_personality_name = string.IsNullOrWhiteSpace(_personality?.Name) ? null : _personality.Name
        });
        return action;
    }

    private static object CardJson(Card c) => new { rank = c.Type.ToString(), suit = c.Suit.ToString() };

    private static PossibleActions BuildPossibleActions(IGetTurnContext context)
    {
        var maxExtraRaise = Math.Max(0, context.MoneyLeft - context.MoneyToCall);
        var canRaise = context.CanRaise && maxExtraRaise > 0;
        var minRaise = Math.Max(1, context.MinRaise);
        if (canRaise)
            minRaise = Math.Min(minRaise, maxExtraRaise);

        var lines = new List<string>();
        var isCheck = context.MoneyToCall <= 0;
        var passiveCode = isCheck ? "CHECK" : "CALL";
        lines.Add(passiveCode);
        if (!context.CanCheck)
            lines.Add("FOLD");

        var raiseMap = new Dictionary<string, int>(StringComparer.Ordinal);
        if (canRaise)
        {
            foreach (var (code, value) in BuildRaiseOptions(minRaise, maxExtraRaise))
            {
                raiseMap[code] = value;
                lines.Add($"{code} ({value})");
            }
        }

        var hasSingle = false;
        var single = PlayerAction.Fold();
        if (!canRaise && context.CanCheck)
        {
            hasSingle = true;
            single = PlayerAction.CheckOrCall();
        }

        return new PossibleActions(
            string.Join('\n', lines),
            canRaise,
            passiveCode,
            raiseMap,
            hasSingle,
            single);
    }

    private static PlayerAction ParseActionCode(string? output, IGetTurnContext context, PossibleActions actions)
    {
        var lastLine = (output ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?
            .Trim()
            .ToUpperInvariant() ?? string.Empty;

        foreach (var (code, amount) in actions.RaiseOptions)
        {
            if (lastLine.Contains(code, StringComparison.OrdinalIgnoreCase))
                return PlayerAction.Raise(amount);
        }

        if (lastLine.Contains(actions.PassiveCode, StringComparison.OrdinalIgnoreCase) || lastLine.Contains("CHECK") || lastLine.Contains("CALL"))
            return PlayerAction.CheckOrCall();
        if (lastLine.Contains("FOLD"))
            return context.CanCheck ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
        return PlayerAction.CheckOrCall();
    }

    private static IReadOnlyList<(string code, int value)> BuildRaiseOptions(int minRaise, int maxRaise)
    {
        if (maxRaise <= minRaise)
            return new List<(string, int)> { ("RAISE_MIN", minRaise), ("RAISE_ALL_IN", maxRaise) };

        var steps = new[]
        {
            ("RAISE_MIN", 0.00),
            ("RAISE_A", 0.01),
            ("RAISE_B", 0.02),
            ("RAISE_C", 0.05),
            ("RAISE_D", 0.10),
            ("RAISE_E", 0.22),
            ("RAISE_F", 0.50),
            ("RAISE_ALL_IN", 1.00)
        };

        var result = new List<(string, int)>();
        var last = minRaise - 1;
        foreach (var (code, ratio) in steps)
        {
            var value = minRaise + (int)Math.Round((maxRaise - minRaise) * ratio);
            value = Math.Clamp(value, minRaise, maxRaise);
            if (value <= last)
                value = Math.Min(maxRaise, last + 1);
            if (value > maxRaise)
                continue;
            if (result.Count > 0 && result[^1].Item2 == value)
                continue;
            result.Add((code, value));
            last = value;
        }

        if (result.Count == 0 || result[0].Item2 != minRaise)
            result.Insert(0, ("RAISE_MIN", minRaise));
        if (result[^1].Item2 != maxRaise)
            result.Add(("RAISE_ALL_IN", maxRaise));

        return result;
    }

    private static IReadOnlyList<Card> SnapshotCards(IEnumerable<Card> source)
    {
        var cards = new List<Card>(5);
        foreach (var card in source)
            cards.Add(card);
        return cards;
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

    private readonly record struct PossibleActions(
        string PossibleActionsString,
        bool CanRaise,
        string PassiveCode,
        IReadOnlyDictionary<string, int> RaiseOptions,
        bool HasSingleLegalAction,
        PlayerAction SingleLegalAction);

    private static string NormalizeRoundName(string round) =>
        round switch
        {
            "PreFlop" => "Pre-Flop",
            _ => round
        };
}
