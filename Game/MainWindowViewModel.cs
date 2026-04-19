using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Players;

namespace PokerApp;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IGameUi
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly Dictionary<string, PlayerRowVm> _seatByName = new(StringComparer.Ordinal);
    private TaskCompletionSource<PlayerAction>? _humanChoice;
    private string _status = string.Empty;
    private string _roundLabel = "";
    private IGetTurnContext? _pendingHumanContext;
    private string _raiseText = "";
    private bool _gameRunning;
    private string _humanPrompt = "When it is your turn, use the buttons below.";
    private bool _humanTurnActive;
    private int _pot;
    private TournamentSession? _session;
    private int _toCall;
    private int _minRaise;
    private string _checkCallButtonText = "Check";
    private bool _canRaiseAction;
    private readonly GameSetupConfig _config;
    private readonly object _replayLock = new();
    private readonly List<JsonElement> _replayEvents = new();
    private int _replayHandBound = -1;
    private CancellationToken _gameCancellation = CancellationToken.None;
    private bool _isReplayMode;
    private bool _replayHasNextAction;
    private bool _replayFinished;
    private string? _replayWinnerName;
    private string _replayWinnersText = "";
    private int _replayCursor;
    private readonly List<ReplayActionStep> _replaySteps = new();
    private readonly Dictionary<string, IReadOnlyList<Card>> _replayBoardByRound = new(StringComparer.Ordinal);

    public MainWindowViewModel() : this(GameSetupConfig.CreateDefault())
    {
    }

    public MainWindowViewModel(GameSetupConfig config)
    {
        _config = config;
        HumanSeat = new PlayerRowVm(GameConstants.HumanPlayerName);
        Seats.Add(HumanSeat);
        foreach (var botInfo in _config.Bots)
        {
            var bot = new PlayerRowVm(botInfo.Name);
            BotSeats.Add(bot);
            Seats.Add(bot);
        }
        foreach (var s in Seats)
            _seatByName[s.Name] = s;
        for (var i = 0; i < 5; i++)
            Board.Add(new CardSlotVm());
        foreach (var s in Seats)
            s.Chips = _config.BuyIn;
    }

    public PlayerRowVm HumanSeat { get; }

    public ObservableCollection<PlayerRowVm> BotSeats { get; } = new();

    public ObservableCollection<PlayerRowVm> Seats { get; } = new();

    public ObservableCollection<CardSlotVm> Board { get; } = new();

    public ObservableCollection<string> HandHistory { get; } = new();
    public ObservableCollection<HandHistoryRoundVm> CurrentHandRounds { get; } = new();

    public IGetTurnContext? PendingHumanContext
    {
        get => _pendingHumanContext;
        private set
        {
            if (!SetField(ref _pendingHumanContext, value))
                return;
            HumanTurnActive = value is not null;
            HumanPrompt = value is null
                ? "When it is your turn, use the buttons below."
                : $"To call: {value.MoneyToCall} · Min raise: {value.MinRaise}";
        }
    }

    public string HumanPrompt
    {
        get => _humanPrompt;
        private set => SetField(ref _humanPrompt, value);
    }

    public bool HumanTurnActive
    {
        get => _humanTurnActive;
        private set
        {
            if (!SetField(ref _humanTurnActive, value))
                return;
            OnPropertyChanged(nameof(CanRaiseNow));
        }
    }

    public string RaiseText
    {
        get => _raiseText;
        set => SetField(ref _raiseText, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string RoundLabel
    {
        get => _roundLabel;
        private set => SetField(ref _roundLabel, value);
    }

    public bool GameRunning
    {
        get => _gameRunning;
        private set => SetField(ref _gameRunning, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? HistoryUiChanged;

    public CancellationToken GameCancellation => _gameCancellation;

    public void AttachGameCancellation(CancellationToken cancellationToken) =>
        _gameCancellation = cancellationToken;

    public int Pot
    {
        get => _pot;
        private set => SetField(ref _pot, value);
    }

    public int ToCall
    {
        get => _toCall;
        private set => SetField(ref _toCall, value);
    }

    public int MinRaise
    {
        get => _minRaise;
        private set => SetField(ref _minRaise, value);
    }

    public string CheckCallButtonText
    {
        get => _checkCallButtonText;
        private set => SetField(ref _checkCallButtonText, value);
    }

    public bool CanRaiseAction
    {
        get => _canRaiseAction;
        private set
        {
            if (!SetField(ref _canRaiseAction, value))
                return;
            OnPropertyChanged(nameof(CanRaiseNow));
        }
    }

    public bool CanRaiseNow => HumanTurnActive && CanRaiseAction;

    public bool IsReplayMode
    {
        get => _isReplayMode;
        private set => SetField(ref _isReplayMode, value);
    }

    public bool ReplayHasNextAction
    {
        get => _replayHasNextAction;
        private set => SetField(ref _replayHasNextAction, value);
    }

    public bool ReplayFinished
    {
        get => _replayFinished;
        private set => SetField(ref _replayFinished, value);
    }

    public string? ReplayWinnerName
    {
        get => _replayWinnerName;
        private set => SetField(ref _replayWinnerName, value);
    }

    public string ReplayWinnersText
    {
        get => _replayWinnersText;
        private set => SetField(ref _replayWinnersText, value);
    }

    public void RunOnUiThread(Action action) => Dispatcher.UIThread.Post(action);

    public void AppendHandHistory(object jsonSerializable)
    {
        var line = JsonSerializer.Serialize(jsonSerializable, JsonOpts);
        lock (_replayLock)
            _replayEvents.Add(JsonSerializer.SerializeToElement(jsonSerializable, JsonOpts));
        RunOnUiThread(() =>
        {
            HandHistory.Insert(0, line);
            while (HandHistory.Count > 500)
                HandHistory.RemoveAt(HandHistory.Count - 1);
        });
    }

    public string BuildHandReplayJson(HandSummary summary)
    {
        lock (_replayLock)
        {
            var events = new JsonArray();
            foreach (var e in _replayEvents)
                events.Add(JsonNode.Parse(e.GetRawText()));

            var root = new JsonObject
            {
                ["schema_version"] = 1,
                ["recorded_at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["hand_number"] = summary.HandNumber,
                ["tournament_finished"] = summary.TournamentFinished,
                ["tournament_winner"] = summary.TournamentWinner,
                ["winners"] = JsonSerializer.SerializeToNode(summary.Winners, JsonOpts),
                ["final_stacks"] = JsonSerializer.SerializeToNode(summary.Stacks, JsonOpts),
                ["tournament"] = JsonSerializer.SerializeToNode(
                    new { buy_in = _config.BuyIn, small_blind = _config.SmallBlind, big_blind = _config.BigBlind },
                    JsonOpts),
                ["events"] = events
            };
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
    }

    public void BeginHandHistory(int handNumber)
    {
        var runUiReset = false;
        lock (_replayLock)
        {
            if (_replayHandBound != handNumber)
            {
                _replayHandBound = handNumber;
                _replayEvents.Clear();
                _replayEvents.Add(JsonSerializer.SerializeToElement(new
                {
                    ev = "replay_header",
                    hand_number = handNumber,
                    tournament = new { buy_in = _config.BuyIn, small_blind = _config.SmallBlind, big_blind = _config.BigBlind },
                    players = BuildReplayPlayersRoster()
                }, JsonOpts));
                runUiReset = true;
            }
        }

        if (!runUiReset)
            return;

        RunOnUiThread(() =>
        {
            CurrentHandRounds.Clear();
            foreach (var seat in Seats)
            {
                seat.ResetForNewHand();
                seat.HideHoles = seat.Name != GameConstants.HumanPlayerName;
                if (seat.Name != GameConstants.HumanPlayerName)
                {
                    seat.Hole1 = null;
                    seat.Hole2 = null;
                }
            }
            HistoryUiChanged?.Invoke();
        });
    }

    public void RecordPublicAction(string playerName, string round, PlayerAction action, int moneyToCall, int moneyLeft, string? promptBeforeAction = null, string? thoughtBeforeAction = null)
    {
        round = NormalizeRoundName(round);
        var humanText = ToHumanAction(action, moneyToCall, moneyLeft);
        RunOnUiThread(() =>
        {
            var section = CurrentHandRounds.LastOrDefault(r => string.Equals(r.RoundName, round, StringComparison.Ordinal))
                          ?? AddRoundSection(round);
            var actionNumber = section.Actions.Count + 1;
            section.Actions.Add(new HandHistoryActionVm(
                $"{actionNumber}. {playerName} {humanText}",
                promptBeforeAction ?? string.Empty,
                thoughtBeforeAction ?? string.Empty,
                showReplayDiagnostics: false));

            if (_seatByName.TryGetValue(playerName, out var row))
            {
                row.LastAction = humanText;
                if (action.Type == PlayerActionType.Fold)
                    row.IsFoldedThisHand = true;
            }

            HistoryUiChanged?.Invoke();
        });
    }

    public void SetStatus(string message) => RunOnUiThread(() => Status = message);

    public void SetCommunityCards(IReadOnlyList<Card> cards)
    {
        RunOnUiThread(() =>
        {
            for (var i = 0; i < Board.Count; i++)
            {
                if (i < cards.Count)
                    Board[i].Image = SvgCardBitmap.TryLoad(cards[i]);
                else
                    Board[i].Image = null;
            }
        });
    }

    public void SetRoundLabel(string label) => RunOnUiThread(() => RoundLabel = label);

    public void SetPot(int pot) => RunOnUiThread(() => Pot = pot);

    public void SetPlayerStack(string playerName, int chips)
    {
        RunOnUiThread(() =>
        {
            if (_seatByName.TryGetValue(playerName, out var row))
            {
                row.Chips = chips;
                row.IsBusted = chips <= 0;
                if (row.IsBusted)
                    row.IsFoldedThisHand = true;
            }
        });
    }

    public void SetCurrentTurn(string? playerName)
    {
        RunOnUiThread(() =>
        {
            foreach (var seat in Seats)
                seat.IsCurrentTurn = string.Equals(seat.Name, playerName, StringComparison.Ordinal);
        });
    }

    public void ClearLastActionsForNewRound()
    {
        RunOnUiThread(() =>
        {
            foreach (var seat in Seats)
                seat.LastAction = string.Empty;
        });
    }

    public void SetHoleCards(string playerName, Card? card1, Card? card2, bool maskOpponentHoles)
    {
        RunOnUiThread(() =>
        {
            if (!_seatByName.TryGetValue(playerName, out var row))
                return;
            var hide = maskOpponentHoles && playerName != GameConstants.HumanPlayerName;
            row.HideHoles = hide;
            row.Hole1 = card1 is not null ? SvgCardBitmap.TryLoad(card1) : null;
            row.Hole2 = card2 is not null ? SvgCardBitmap.TryLoad(card2) : null;
        });
    }

    public void RevealShowdown(IReadOnlyDictionary<string, IReadOnlyList<Card>> holeCardsByPlayer)
    {
        RunOnUiThread(() =>
        {
            foreach (var (name, cards) in holeCardsByPlayer)
            {
                if (!_seatByName.TryGetValue(name, out var row))
                    continue;
                row.HideHoles = false;
                if (cards.Count > 0)
                    row.Hole1 = SvgCardBitmap.TryLoad(cards[0]);
                if (cards.Count > 1)
                    row.Hole2 = SvgCardBitmap.TryLoad(cards[1]);
            }
        });
    }

    public void RevealAllHoleCards()
    {
        RunOnUiThread(() =>
        {
            foreach (var seat in Seats)
                seat.HideHoles = false;
        });
    }

    public void InitializeTournament()
    {
        IsReplayMode = false;
        ReplayHasNextAction = false;
        ReplayFinished = false;
        ReplayWinnerName = null;
        ReplayWinnersText = "";
        _replayCursor = 0;
        _replaySteps.Clear();
        lock (_replayLock)
        {
            _replayEvents.Clear();
            _replayHandBound = -1;
        }

        HandHistory.Clear();
        SetStatus(string.Empty);
        SetRoundLabel(string.Empty);
        SetPot(0);
        ToCall = 0;
        MinRaise = 0;
        CheckCallButtonText = "Check";
        CanRaiseAction = false;
        SetCommunityCards(Array.Empty<Card>());
        PendingHumanContext = null;
        CurrentHandRounds.Clear();
        foreach (var seat in Seats)
            seat.IsReplayMode = false;
        var ui = this;
        var players = new List<IPlayer> { new HumanPlayer(ui, WaitHumanAction) };
        OpenAiCompatClient? llmClient = null;
        foreach (var botInfo in _config.Bots)
        {
            players.Add(botInfo.Type switch
            {
                BotType.LlmBotPlayer => new LlmBotPlayer(
                    botInfo.Name,
                    ui,
                    llmClient ??= new OpenAiCompatClient(
                        _config.LlmApiUrl ?? "http://127.0.0.1:8111/v1",
                        _config.LlmApiKey ?? string.Empty,
                        _config.LlmModel,
                        _config.LlmTemperature),
                    _config.BigBlind,
                    botInfo.LlmPersonality),
                BotType.RandomBotPlayer => new RandomBotPlayer(botInfo.Name, ui),
                _ => new RandomBotPlayer(botInfo.Name, ui),
            });
        }

        _session = new TournamentSession(players, _config.BuyIn, _config.SmallBlind);
        foreach (var (name, stack) in GetCurrentStacks())
            SetPlayerStack(name, stack);
    }

    public void InitializeReplay(string replayJson)
    {
        IsReplayMode = true;
        ReplayHasNextAction = false;
        ReplayFinished = false;
        ReplayWinnerName = null;
        ReplayWinnersText = "";
        _replayCursor = 0;
        _replaySteps.Clear();
        _replayBoardByRound.Clear();
        HandHistory.Clear();
        CurrentHandRounds.Clear();
        SetStatus(string.Empty);
        SetRoundLabel(string.Empty);
        SetPot(0);
        SetCommunityCards(Array.Empty<Card>());
        PendingHumanContext = null;
        ToCall = 0;
        MinRaise = 0;
        CheckCallButtonText = "Check";
        CanRaiseAction = false;
        foreach (var seat in Seats)
        {
            seat.ResetForNewHand();
            seat.IsReplayMode = true;
        }

        try
        {
            using var doc = JsonDocument.Parse(replayJson);
            if (doc.RootElement.TryGetProperty("tournament_winner", out var tw))
                ReplayWinnerName = tw.GetString();
            if (doc.RootElement.TryGetProperty("winners", out var winnersEl) && winnersEl.ValueKind == JsonValueKind.Array)
            {
                var names = winnersEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
                ReplayWinnersText = string.Join(", ", names);
            }
            if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                return;
            foreach (var ev in events.EnumerateArray())
            {
                if (!ev.TryGetProperty("ev", out var typeEl))
                    continue;
                var type = typeEl.GetString() ?? string.Empty;
                if (type == "start_hand")
                {
                    var player = ev.TryGetProperty("player", out var pEl) ? pEl.GetString() ?? string.Empty : string.Empty;
                    var stack = ev.TryGetProperty("stack", out var sEl) && sEl.TryGetInt32(out var st) ? st : _config.BuyIn;
                    if (!string.IsNullOrWhiteSpace(player))
                        SetPlayerStack(player, stack);
                    if (!string.IsNullOrWhiteSpace(player) && ev.TryGetProperty("cards", out var cardsEl) && cardsEl.ValueKind == JsonValueKind.Array)
                    {
                        var cards = ParseCards(cardsEl);
                        var c1 = cards.Count > 0 ? cards[0] : (Card?)null;
                        var c2 = cards.Count > 1 ? cards[1] : (Card?)null;
                        SetHoleCards(player, c1, c2, maskOpponentHoles: false);
                    }
                }
                if (type == "start_round")
                {
                    var roundName = ev.TryGetProperty("round", out var rr) ? rr.GetString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrWhiteSpace(roundName) &&
                        ev.TryGetProperty("community_cards", out var cc) &&
                        cc.ValueKind == JsonValueKind.Array)
                    {
                        _replayBoardByRound[NormalizeRoundName(roundName)] = ParseCards(cc);
                    }
                }
                if (type != "action")
                    continue;
                var playerName = ev.TryGetProperty("player", out var pn) ? pn.GetString() ?? string.Empty : string.Empty;
                var round = ev.TryGetProperty("round", out var rd) ? rd.GetString() ?? "PreFlop" : "PreFlop";
                var actionText = ev.TryGetProperty("action", out var ac) ? ac.GetString() ?? string.Empty : string.Empty;
                var moneyToCall = ev.TryGetProperty("moneyToCall", out var mtc) && mtc.TryGetInt32(out var m) ? m : 0;
                var moneyLeft = ev.TryGetProperty("stack", out var ml) && ml.TryGetInt32(out var l) ? l : 0;
                var pot = ev.TryGetProperty("pot", out var pe) && pe.TryGetInt32(out var pp) ? pp : 0;
                var prompt = ev.TryGetProperty("prompt_before_action", out var pr) ? pr.GetString() : null;
                var thought = ev.TryGetProperty("thought_before_action", out var th) ? th.GetString() : null;
                _replaySteps.Add(new ReplayActionStep(playerName, round, actionText, moneyToCall, moneyLeft, pot, prompt, thought));
            }
            ReplayHasNextAction = _replaySteps.Count > 0;
        }
        catch
        {
            ReplayHasNextAction = false;
        }
    }

    public void AdvanceReplay()
    {
        if (!IsReplayMode || _replayCursor >= _replaySteps.Count)
        {
            ReplayHasNextAction = false;
            return;
        }

        var step = _replaySteps[_replayCursor++];
        SetCurrentTurn(step.PlayerName);
        var round = NormalizeRoundName(step.Round);
        if (_replayBoardByRound.TryGetValue(round, out var board))
            SetCommunityCards(board);
        var humanText = ToHumanActionFromReplay(step.Action, step.MoneyToCall, step.MoneyLeft);
        RunOnUiThread(() =>
        {
            var section = CurrentHandRounds.LastOrDefault(r => string.Equals(r.RoundName, round, StringComparison.Ordinal))
                          ?? AddRoundSection(round);
            var actionNumber = section.Actions.Count + 1;
            section.Actions.Add(new HandHistoryActionVm(
                $"{actionNumber}. {step.PlayerName} {humanText}",
                step.PromptBeforeAction ?? string.Empty,
                step.ThoughtBeforeAction ?? string.Empty,
                showReplayDiagnostics: true));
            if (_seatByName.TryGetValue(step.PlayerName, out var row))
                row.LastAction = humanText;
            HistoryUiChanged?.Invoke();
        });
        var contributed = EstimateContributionFromReplay(step.Action, step.MoneyToCall, step.MoneyLeft);
        SetPlayerStack(step.PlayerName, Math.Max(0, step.MoneyLeft - contributed));
        SetPot(step.Pot + contributed);
        ReplayHasNextAction = _replayCursor < _replaySteps.Count;
        if (!ReplayHasNextAction)
            ReplayFinished = true;
    }

    public async Task<HandSummary> PlayNextHandAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null)
            throw new InvalidOperationException("Tournament is not initialized.");
        if (GameRunning)
            throw new InvalidOperationException("Hand already running.");

        GameRunning = true;
        try
        {
            var summary = await Task.Run(() => _session.PlayNextHand(cancellationToken), cancellationToken);
            foreach (var (name, stack) in summary.Stacks)
                SetPlayerStack(name, stack);
            SetStatus(string.Empty);
            AppendHandHistory(new
            {
                ev = "hand_end",
                hand = summary.HandNumber,
                winners = summary.Winners,
                stacks = summary.Stacks,
                tournamentFinished = summary.TournamentFinished,
                tournamentWinner = summary.TournamentWinner
            });

            return summary;
        }
        finally
        {
            GameRunning = false;
        }
    }

    public void PrepareForNextHandVisuals()
    {
        RunOnUiThread(() =>
        {
            SetCommunityCards(Array.Empty<Card>());
            SetPot(0);
            foreach (var seat in BotSeats)
            {
                seat.HideHoles = true;
                seat.Hole1 = null;
                seat.Hole2 = null;
            }
        });
    }

    private PlayerAction WaitHumanAction(IGetTurnContext context)
    {
        var tcs = new TaskCompletionSource<PlayerAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = _gameCancellation.Register(() =>
        {
            RunOnUiThread(() =>
            {
                PendingHumanContext = null;
                _humanChoice = null;
                CanRaiseAction = false;
                tcs.TrySetResult(PlayerAction.Fold());
            });
        });
        RunOnUiThread(() =>
        {
            PendingHumanContext = context;
            _humanChoice = tcs;
            RaiseText = context.MinRaise.ToString();
            ToCall = context.MoneyToCall;
            MinRaise = context.MinRaise;
            CheckCallButtonText = context.CanCheck ? "Check" : "Call";
            CanRaiseAction = context.CanRaise;
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    private object[] BuildReplayPlayersRoster()
    {
        var list = new List<object>
        {
            new
            {
                name = GameConstants.HumanPlayerName,
                player_type = "HUMAN",
                llm_personality_id = (int?)null,
                llm_personality_name = (string?)null
            }
        };
        foreach (var b in _config.Bots)
        {
            list.Add(new
            {
                name = b.Name,
                player_type = b.Type == BotType.LlmBotPlayer ? "LLM_AGENT" : "RANDOM_BOT",
                llm_personality_id = b.LlmPersonality?.Id,
                llm_personality_name = string.IsNullOrWhiteSpace(b.LlmPersonality?.Name) ? null : b.LlmPersonality.Name
            });
        }

        return list.ToArray();
    }

    public void HumanSubmitFold()
    {
        var t = _humanChoice;
        PendingHumanContext = null;
        _humanChoice = null;
        CanRaiseAction = false;
        t?.TrySetResult(PlayerAction.Fold());
    }

    public void HumanSubmitCheckCall()
    {
        var t = _humanChoice;
        PendingHumanContext = null;
        _humanChoice = null;
        CanRaiseAction = false;
        t?.TrySetResult(PlayerAction.CheckOrCall());
    }

    public void HumanSubmitRaise()
    {
        var t = _humanChoice;
        var ctx = PendingHumanContext;
        PendingHumanContext = null;
        _humanChoice = null;
        CanRaiseAction = false;
        if (t is null || ctx is null)
            return;
        if (!int.TryParse(RaiseText, out var amt) || amt <= 0)
            amt = ctx.MinRaise;
        t.TrySetResult(PlayerAction.Raise(amt));
    }

    public IReadOnlyDictionary<string, int> GetCurrentStacks()
    {
        return Seats.ToDictionary(s => s.Name, s => s.Chips, StringComparer.Ordinal);
    }

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private static string ToHumanAction(PlayerAction action, int moneyToCall, int moneyLeft)
    {
        return action.Type switch
        {
            PlayerActionType.Fold => "folds",
            PlayerActionType.CheckCall when moneyToCall <= 0 => "checks",
            PlayerActionType.CheckCall => $"calls {moneyToCall}",
            PlayerActionType.Raise when moneyLeft > 0 && moneyToCall + Math.Max(0, action.Money) >= moneyLeft => "raises ALL IN",
            PlayerActionType.Raise => $"raises by {action.Money}",
            PlayerActionType.Post => $"posts {action.Money}",
            _ => action.ToString()
        };
    }

    private HandHistoryRoundVm AddRoundSection(string round)
    {
        var section = new HandHistoryRoundVm(round);
        CurrentHandRounds.Add(section);
        return section;
    }

    public void ToggleHistoryActionPanel(HandHistoryActionVm target, bool showPrompt)
    {
        RunOnUiThread(() =>
        {
            var alreadyOpen = showPrompt ? target.IsPromptExpanded : target.IsThoughtExpanded;
            foreach (var section in CurrentHandRounds)
            {
                foreach (var action in section.Actions)
                {
                    action.IsPromptExpanded = false;
                    action.IsThoughtExpanded = false;
                }
            }

            if (!alreadyOpen)
            {
                if (showPrompt)
                    target.IsPromptExpanded = true;
                else
                    target.IsThoughtExpanded = true;
            }
        });
    }

    private static string NormalizeRoundName(string round) =>
        round switch
        {
            "PreFlop" => "Pre-Flop",
            _ => round
        };

    private static string ToHumanActionFromReplay(string actionText, int moneyToCall, int moneyLeft)
    {
        if (actionText.Contains("Fold", StringComparison.OrdinalIgnoreCase))
            return "folds";
        if (actionText.Contains("CheckOrCall", StringComparison.OrdinalIgnoreCase))
            return moneyToCall <= 0 ? "checks" : $"calls {moneyToCall}";
        if (actionText.Contains("Raise", StringComparison.OrdinalIgnoreCase))
        {
            var raise = ParseRaiseAmount(actionText);
            if (moneyLeft > 0 && moneyToCall + Math.Max(0, raise) >= moneyLeft)
                return "raises ALL IN";
            return $"raises by {raise}";
        }
        if (actionText.Contains("Post", StringComparison.OrdinalIgnoreCase))
            return $"posts {ParseRaiseAmount(actionText)}";
        return actionText;
    }

    private static int EstimateContributionFromReplay(string actionText, int moneyToCall, int moneyLeft)
    {
        if (actionText.Contains("Fold", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (actionText.Contains("CheckOrCall", StringComparison.OrdinalIgnoreCase))
            return Math.Min(moneyLeft, moneyToCall);
        if (actionText.Contains("Raise", StringComparison.OrdinalIgnoreCase))
            return Math.Min(moneyLeft, moneyToCall + Math.Max(0, ParseRaiseAmount(actionText)));
        return 0;
    }

    private static int ParseRaiseAmount(string actionText)
    {
        var match = Regex.Match(actionText, @"\((\d+)\)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
            return value;
        return 0;
    }

    private static List<Card> ParseCards(JsonElement cardsEl)
    {
        var result = new List<Card>(2);
        foreach (var c in cardsEl.EnumerateArray())
        {
            if (!c.TryGetProperty("rank", out var rankEl) || !c.TryGetProperty("suit", out var suitEl))
                continue;
            var rankText = rankEl.GetString() ?? string.Empty;
            var suitText = suitEl.GetString() ?? string.Empty;
            if (!Enum.TryParse<CardType>(rankText, true, out var rank))
                continue;
            if (!Enum.TryParse<CardSuit>(suitText, true, out var suit))
                continue;
            result.Add(new Card(suit, rank));
        }
        return result;
    }

    private sealed record ReplayActionStep(
        string PlayerName,
        string Round,
        string Action,
        int MoneyToCall,
        int MoneyLeft,
        int Pot,
        string? PromptBeforeAction,
        string? ThoughtBeforeAction);
}
