using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public partial class MainMenuWindow : Window
{
    private sealed class ReplayHandListItem
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string ReplayJson { get; init; } = "";
        public string DisplayLabel { get; init; } = "";
        public override string ToString() => DisplayLabel;
    }

    private sealed class PersonalityPickItem
    {
        public int? Id { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Label { get; init; } = "";

        public override string ToString() => Label;
    }

    private MainWindow? _gameView;
    private Bitmap? _menuBackgroundBitmap;
    private TaskCompletionSource<bool>? _handContinueTcs;
    private CancellationTokenSource? _gameCts;
    private GameSetupConfig? _activeGameConfig;
    private string? _pendingReplayJson;
    private bool _handSaveInFlight;
    private bool _showingReplayHandResult;

    public MainMenuWindow()
    {
        InitializeComponent();
        WindowState = WindowState.Maximized;
        BuyInTextBox.Text = GameConstants.DefaultBuyIn.ToString();
        SmallBlindTextBox.Text = GameConstants.DefaultSmallBlind.ToString();
        BotCountComboBox.SelectedIndex = 2;
        Bot1TypeComboBox.SelectedIndex = 0;
        Bot2TypeComboBox.SelectedIndex = 0;
        Bot3TypeComboBox.SelectedIndex = 0;
        SetDefaultBotNames();
        ApiUrlTextBox.Text = "https://api.groq.com/openai/v1";
        ModelTextBox.Text = "qwen/qwen3-32b";
        ApiKeyTextBox.Text = "";
        RefreshBigBlindInfo();
        RefreshBotRows();
        RefreshLlmConfigState();
        RefreshPersonalityComboBoxes();
        LoadMenuBackground();
        Closed += (_, _) => _menuBackgroundBitmap?.Dispose();
    }

    private void OnStartGamePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gameView is not null)
            return;
        SetupErrorTextBlock.Text = string.Empty;
        RefreshPersonalityComboBoxes();
        SetOverlay(setupVisible: true, winnerVisible: false, handVisible: false);
    }

    private void OnExitPressed(object? sender, PointerPressedEventArgs e) => Close();

    private void OnPersonalitiesPressed(object? sender, PointerPressedEventArgs e)
    {
        PersonalitiesHost.Content = new PersonalitiesView { NavigateBack = LeavePersonalitiesView };
        MenuLayer.IsVisible = false;
        PersonalitiesHost.IsVisible = true;
    }

    private async void OnViewReplayPressed(object? sender, PointerPressedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        await LoadReplayListAsync();
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, replayListVisible: true);
    }

    private void OnCloseReplayListPressed(object? sender, PointerPressedEventArgs e)
    {
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, replayListVisible: false);
    }

    private void OnOpenReplayPressed(object? sender, PointerPressedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        if (ReplayHandsListBox.SelectedItem is not ReplayHandListItem item)
        {
            ReplayErrorTextBlock.Text = "Select a replay first.";
            return;
        }

        _gameView = BuildReplayView(item);
        GameHost.Content = _gameView;
        GameHost.IsVisible = true;
        MenuLayer.IsVisible = false;
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, replayListVisible: false);
    }

    private void LeavePersonalitiesView()
    {
        PersonalitiesHost.Content = null;
        PersonalitiesHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        RefreshPersonalityComboBoxes();
    }

    private void OnCancelSetupPressed(object? sender, PointerPressedEventArgs e)
    {
        SetupErrorTextBlock.Text = string.Empty;
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false);
    }

    private async void OnConfirmSetupPressed(object? sender, PointerPressedEventArgs e)
    {
        SetupErrorTextBlock.Text = string.Empty;

        if (!int.TryParse(BuyInTextBox.Text, out var buyIn) || buyIn <= 0)
        {
            SetupErrorTextBlock.Text = "Buy-in must be a positive number.";
            return;
        }

        if (!int.TryParse(SmallBlindTextBox.Text, out var smallBlind) || smallBlind <= 0)
        {
            SetupErrorTextBlock.Text = "Small blind must be a positive number.";
            return;
        }

        if (smallBlind * 20 > buyIn)
        {
            SetupErrorTextBlock.Text = "Small blind must be at least 20x smaller than buy-in.";
            return;
        }

        var botCount = GetSelectedBotCount();
        if (botCount < 1 || botCount > 3)
        {
            SetupErrorTextBlock.Text = "Choose between 1 and 3 bots.";
            return;
        }

        var bots = new List<BotSetup>();
        for (var i = 0; i < botCount; i++)
        {
            var botType = GetBotTypeForIndex(i);
            var botName = GetBotNameForIndex(i, botType);
            if (string.IsNullOrWhiteSpace(botName))
            {
                SetupErrorTextBlock.Text = $"Bot {i + 1} name cannot be empty.";
                return;
            }
            var personality = GetLlmPersonalitySnapshotForBotIndex(i);
            bots.Add(new BotSetup(botName, botType, personality));
        }

        var usesLlm = bots.Any(b => b.Type == BotType.LlmBotPlayer);
        var apiUrl = ApiUrlTextBox.Text?.Trim();
        var apiKey = ApiKeyTextBox.Text?.Trim();
        var model = ModelTextBox.Text?.Trim();
        if (usesLlm)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                SetupErrorTextBlock.Text = "Provide API URL when at least one LLM bot is selected.";
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                SetupErrorTextBlock.Text = "Provide model name when at least one LLM bot is selected.";
                return;
            }
        }

        var config = new GameSetupConfig(
            buyIn,
            smallBlind,
            bots,
            LlmApiUrl: apiUrl,
            LlmApiKey: apiKey,
            LlmModel: string.IsNullOrWhiteSpace(model) ? "qwen/qwen3-32b" : model,
            LlmTemperature: 0);

        _activeGameConfig = config;
        _gameCts = new CancellationTokenSource();
        _gameView = new MainWindow(config);
        _gameView.TournamentFinished += OnTournamentFinished;
        _gameView.ExitToMenuRequested += OnGameExitToMenuRequested;
        _gameView.HandFinishedAsync = OnHandFinishedAsync;
        GameHost.Content = _gameView;
        MenuLayer.IsVisible = false;
        GameHost.IsVisible = true;
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false);

        try
        {
            await _gameView.StartGameLoopAsync(_gameCts.Token);
        }
        catch (OperationCanceledException)
        {
            FinishAbortedGameToMenu();
        }
        finally
        {
            if (_gameView is not null)
                _gameView.ExitToMenuRequested -= OnGameExitToMenuRequested;
        }
    }


    private void OnGameExitToMenuRequested()
    {
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, exitConfirmVisible: true);
    }

    private void OnExitConfirmCancelPressed(object? sender, PointerPressedEventArgs e) =>
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, exitConfirmVisible: false);

    private void OnExitConfirmYesPressed(object? sender, PointerPressedEventArgs e)
    {
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, exitConfirmVisible: false);
        _gameCts?.Cancel();
    }

    private void FinishAbortedGameToMenu()
    {
        if (_gameView is not null)
        {
            _gameView.TournamentFinished -= OnTournamentFinished;
            _gameView.ExitToMenuRequested -= OnGameExitToMenuRequested;
        }

        _gameView = null;
        GameHost.Content = null;
        GameHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, exitConfirmVisible: false);
        _gameCts?.Dispose();
        _gameCts = null;
        _activeGameConfig = null;
        _pendingReplayJson = null;
        _showingReplayHandResult = false;
    }

    private void OnTournamentFinished(string winnerName)
    {
        WinnerText.Text = $"Winner: {winnerName}";
        SetOverlay(setupVisible: false, winnerVisible: true, handVisible: false);
    }

    private void OnBackToMenuPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gameView is not null)
        {
            _gameView.TournamentFinished -= OnTournamentFinished;
            _gameView.ReplayHandFinished -= OnReplayHandFinished;
        }
        _gameView = null;
        GameHost.Content = null;
        GameHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false);
        _gameCts?.Dispose();
        _gameCts = null;
        _activeGameConfig = null;
        _showingReplayHandResult = false;
    }

    private void OnSmallBlindChanged(object? sender, TextChangedEventArgs e) => RefreshBigBlindInfo();

    private void OnBotCountChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshBotRows();
        RefreshLlmConfigState();
    }

    private void OnBotTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        SyncDefaultBotNamesToTypes();
        RefreshLlmConfigState();
        RefreshPersonalityComboEnabledState();
    }

    private int GetSelectedBotCount()
    {
        if (BotCountComboBox.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Content?.ToString(), out var value))
        {
            return value;
        }

        return 1;
    }

    private void RefreshBigBlindInfo()
    {
        if (int.TryParse(SmallBlindTextBox.Text, out var smallBlind) && smallBlind > 0)
            BigBlindInfoText.Text = $"{smallBlind * 2} (always x2 small blind)";
        else
            BigBlindInfoText.Text = "Invalid small blind value";
    }

    private void RefreshBotRows()
    {
        var count = GetSelectedBotCount();
        Bot1Row.IsVisible = count >= 1;
        Bot2Row.IsVisible = count >= 2;
        Bot3Row.IsVisible = count >= 3;
        RefreshPersonalityComboEnabledState();
    }

    private void RefreshPersonalityComboBoxes()
    {
        var items = new List<PersonalityPickItem>
        {
            new() { Label = "(None)" }
        };
        try
        {
            PokerDbBootstrap.EnsureInitialized();
            using var db = PokerDbBootstrap.CreateContext();
            foreach (var p in db.LlmAgentPersonalities.AsNoTracking().OrderBy(x => x.Name))
            {
                items.Add(new PersonalityPickItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.PersonalityDescription,
                    Label = p.Name
                });
            }
        }
        catch
        {
        }

        var arr = items.ToArray();
        Bot1PersonalityComboBox.ItemsSource = arr;
        Bot2PersonalityComboBox.ItemsSource = arr;
        Bot3PersonalityComboBox.ItemsSource = arr;
        Bot1PersonalityComboBox.SelectedIndex = 0;
        Bot2PersonalityComboBox.SelectedIndex = 0;
        Bot3PersonalityComboBox.SelectedIndex = 0;
        RefreshPersonalityComboEnabledState();
    }

    private void RefreshPersonalityComboEnabledState()
    {
        var count = GetSelectedBotCount();
        Bot1PersonalityComboBox.IsEnabled = count >= 1 && GetBotTypeForIndex(0) == BotType.LlmBotPlayer;
        Bot2PersonalityComboBox.IsEnabled = count >= 2 && GetBotTypeForIndex(1) == BotType.LlmBotPlayer;
        Bot3PersonalityComboBox.IsEnabled = count >= 3 && GetBotTypeForIndex(2) == BotType.LlmBotPlayer;
    }

    private void RefreshLlmConfigState()
    {
        var usesLlm = AnyVisibleLlmBotSelected();
        LlmConfigPanel.IsEnabled = usesLlm;
        LlmConfigPanel.Opacity = usesLlm ? 1 : 0.45;
    }

    private async Task OnHandFinishedAsync(HandSummary summary, string replayJson, CancellationToken cancellationToken)
    {
        _showingReplayHandResult = false;
        var winners = string.Join(", ", summary.Winners);
        HandWinnerText.Text = $"Winner(s): {winners}";
        HandResultActionText.Text = "Next";
        _pendingReplayJson = replayJson;
        HandSaveRow.IsVisible = true;
        ResetHandSaveUi();
        _handContinueTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: true);
        try
        {
            await _handContinueTcs.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false);
    }

    private void ResetHandSaveUi()
    {
        _handSaveInFlight = false;
        HandSaveActivePanel.IsVisible = true;
        HandSavedLabel.IsVisible = false;
        HandSaveErrorTextBlock.Text = string.Empty;
        HandNameTextBox.Text = string.Empty;
        SaveHandCheckBox.IsChecked = false;
        SaveHandCheckBox.IsEnabled = true;
    }

    private async void OnSaveHandCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (SaveHandCheckBox.IsChecked != true || _handSaveInFlight)
            return;
        if (string.IsNullOrEmpty(_pendingReplayJson) || _activeGameConfig is null)
            return;
        var handName = HandNameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(handName))
        {
            HandSaveErrorTextBlock.Text = "Hand name is required.";
            SaveHandCheckBox.IsChecked = false;
            return;
        }

        _handSaveInFlight = true;
        SaveHandCheckBox.IsEnabled = false;
        try
        {
            var seats = BuildSeatRowsForSave(_activeGameConfig);
            await HandPersistence.SaveAsync(handName, _pendingReplayJson, seats);
            HandSaveActivePanel.IsVisible = false;
            HandSavedLabel.IsVisible = true;
        }
        catch
        {
            SaveHandCheckBox.IsEnabled = true;
            SaveHandCheckBox.IsChecked = false;
            _handSaveInFlight = false;
        }
    }

    private void OnSaveHandLabelPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!HandSavedLabel.IsVisible)
            SaveHandCheckBox.IsChecked = !(SaveHandCheckBox.IsChecked ?? false);
    }

    private static List<(string Name, string PlayerType, int? LlmPersonalityId)> BuildSeatRowsForSave(GameSetupConfig config)
    {
        var list = new List<(string, string, int?)>
        {
            (GameConstants.HumanPlayerName, "HUMAN", null)
        };
        foreach (var b in config.Bots)
        {
            var t = b.Type == BotType.LlmBotPlayer ? "LLM_AGENT" : "RANDOM_BOT";
            list.Add((b.Name, t, b.LlmPersonality?.Id));
        }

        return list;
    }

    private void OnNextHandPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_showingReplayHandResult)
        {
            OnBackToMenuPressed(sender, e);
            return;
        }
        _handContinueTcs?.TrySetResult(true);
        _handContinueTcs = null;
    }

    private void SetOverlay(bool setupVisible, bool winnerVisible, bool handVisible, bool exitConfirmVisible = false, bool replayListVisible = false)
    {
        SetupOverlay.IsVisible = setupVisible;
        WinnerOverlay.IsVisible = winnerVisible;
        HandResultOverlay.IsVisible = handVisible;
        ExitConfirmOverlay.IsVisible = exitConfirmVisible;
        ReplayListOverlay.IsVisible = replayListVisible;
        OverlayShade.IsVisible = setupVisible || winnerVisible || handVisible || exitConfirmVisible || replayListVisible;
    }

    private ComboBox BotPersonalityComboForIndex(int botIndex) =>
        botIndex switch
        {
            0 => Bot1PersonalityComboBox,
            1 => Bot2PersonalityComboBox,
            2 => Bot3PersonalityComboBox,
            _ => Bot1PersonalityComboBox
        };

    private LlmPersonalitySnapshot? GetLlmPersonalitySnapshotForBotIndex(int botIndex)
    {
        if (GetBotTypeForIndex(botIndex) != BotType.LlmBotPlayer)
            return null;
        var combo = BotPersonalityComboForIndex(botIndex);
        if (combo.SelectedItem is PersonalityPickItem pick && pick.Id != null)
            return new LlmPersonalitySnapshot(pick.Id, pick.Name, pick.Description);
        return null;
    }

    private BotType GetBotTypeForIndex(int botIndex)
    {
        var combo = botIndex switch
        {
            0 => Bot1TypeComboBox,
            1 => Bot2TypeComboBox,
            2 => Bot3TypeComboBox,
            _ => Bot1TypeComboBox
        };
        var content = (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return string.Equals(content, "LlmBotPlayer", StringComparison.Ordinal)
            ? BotType.LlmBotPlayer
            : BotType.RandomBotPlayer;
    }

    private bool AnyVisibleLlmBotSelected()
    {
        var count = GetSelectedBotCount();
        for (var i = 0; i < count; i++)
        {
            if (GetBotTypeForIndex(i) == BotType.LlmBotPlayer)
                return true;
        }
        return false;
    }

    private string GetBotNameForIndex(int botIndex, BotType type)
    {
        var textBox = botIndex switch
        {
            0 => Bot1NameTextBox,
            1 => Bot2NameTextBox,
            2 => Bot3NameTextBox,
            _ => Bot1NameTextBox
        };

        var value = textBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return BuildDefaultBotName(type, botIndex);
    }

    private void SetDefaultBotNames()
    {
        SyncDefaultBotNamesToTypes();
    }

    private static string BuildDefaultBotName(BotType type, int index) =>
        $"{type}_{index + 1}";

    private static void SyncBotNameToType(TextBox textBox, int index, BotType? selectedType = null)
    {
        var type = selectedType ?? BotType.RandomBotPlayer;
        var current = textBox.Text?.Trim();
        var randomDefault = BuildDefaultBotName(BotType.RandomBotPlayer, index);
        var llmDefault = BuildDefaultBotName(BotType.LlmBotPlayer, index);

        if (string.IsNullOrWhiteSpace(current) || current == randomDefault || current == llmDefault)
            textBox.Text = BuildDefaultBotName(type, index);
    }

    private void SyncDefaultBotNamesToTypes()
    {
        SyncBotNameToType(Bot1NameTextBox, 0, GetBotTypeForIndex(0));
        SyncBotNameToType(Bot2NameTextBox, 1, GetBotTypeForIndex(1));
        SyncBotNameToType(Bot3NameTextBox, 2, GetBotTypeForIndex(2));
    }

    private void LoadMenuBackground()
    {
        var backgroundPath = Path.Combine(AppContext.BaseDirectory, "assets", "backgrounds", "main_menu.jpg");
        if (!File.Exists(backgroundPath))
            return;

        try
        {
            _menuBackgroundBitmap = new Bitmap(backgroundPath);
            MenuBackgroundImage.Source = _menuBackgroundBitmap;
        }
        catch
        {
        }
    }

    private async Task LoadReplayListAsync()
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var hands = await db.SavedHands
            .AsNoTracking()
            .OrderByDescending(h => h.HandTimeIso)
            .Select(h => new ReplayHandListItem
            {
                Id = h.Id,
                Name = h.HandName,
                ReplayJson = h.HandHistoryJson,
                DisplayLabel = $"{FormatReplayTime(h.HandTimeIso)} - {h.HandName}"
            })
            .ToListAsync();
        ReplayHandsListBox.ItemsSource = hands;
        ReplayHandsListBox.SelectedIndex = hands.Count > 0 ? 0 : -1;
    }

    private async void OnDeleteReplayClick(object? sender, RoutedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        if (sender is not Control { DataContext: ReplayHandListItem item })
        {
            ReplayErrorTextBlock.Text = "Could not resolve selected replay.";
            return;
        }

        try
        {
            PokerDbBootstrap.EnsureInitialized();
            await using var db = PokerDbBootstrap.CreateContext();
            var hand = await db.SavedHands.FirstOrDefaultAsync(h => h.Id == item.Id);
            if (hand is null)
            {
                ReplayErrorTextBlock.Text = "Replay no longer exists.";
                await LoadReplayListAsync();
                return;
            }
            db.SavedHands.Remove(hand);
            await db.SaveChangesAsync();
            await LoadReplayListAsync();
        }
        catch
        {
            ReplayErrorTextBlock.Text = "Failed to delete replay.";
        }
    }

    private static string FormatReplayTime(string handTimeIso)
    {
        if (DateTimeOffset.TryParse(handTimeIso, out var dto))
            return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return handTimeIso;
    }

    private MainWindow BuildReplayView(ReplayHandListItem item)
    {
        var defaultBuyIn = GameConstants.DefaultBuyIn;
        var defaultSmallBlind = GameConstants.DefaultSmallBlind;
        var bots = new List<BotSetup>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(item.ReplayJson);
            if (doc.RootElement.TryGetProperty("tournament", out var t))
            {
                if (t.TryGetProperty("buy_in", out var buyInEl) && buyInEl.TryGetInt32(out var buyIn))
                    defaultBuyIn = buyIn;
                if (t.TryGetProperty("small_blind", out var sbEl) && sbEl.TryGetInt32(out var sb))
                    defaultSmallBlind = sb;
            }
            if (doc.RootElement.TryGetProperty("events", out var events) && events.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var ev in events.EnumerateArray())
                {
                    if (!ev.TryGetProperty("ev", out var evType) || evType.GetString() != "replay_header")
                        continue;
                    if (!ev.TryGetProperty("players", out var players) || players.ValueKind != System.Text.Json.JsonValueKind.Array)
                        continue;
                    foreach (var p in players.EnumerateArray())
                    {
                        var playerName = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var typeText = p.TryGetProperty("player_type", out var pt) ? pt.GetString() ?? "" : "";
                        if (string.Equals(playerName, GameConstants.HumanPlayerName, StringComparison.Ordinal))
                            continue;
                        var botType = string.Equals(typeText, "LLM_AGENT", StringComparison.OrdinalIgnoreCase)
                            ? BotType.LlmBotPlayer
                            : BotType.RandomBotPlayer;
                        bots.Add(new BotSetup(playerName, botType));
                    }
                    break;
                }
            }
        }
        catch
        {
        }

        if (bots.Count == 0)
            bots.Add(new BotSetup("ReplayBot", BotType.RandomBotPlayer));

        var config = new GameSetupConfig(defaultBuyIn, defaultSmallBlind, bots);
        var view = new MainWindow(config, isReplayMode: true);
        view.ReplayHandFinished += OnReplayHandFinished;
        view.ExitToMenuRequested += OnReplayExitRequested;
        view.LoadReplay(item.ReplayJson);
        return view;
    }

    private void OnReplayHandFinished(string winnerName)
    {
        _showingReplayHandResult = true;
        HandSaveRow.IsVisible = false;
        HandResultActionText.Text = "Back to main menu";
        HandWinnerText.Text = $"Winner: {winnerName}";
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: true);
    }

    private void OnReplayExitRequested()
    {
        if (_gameView is not null)
        {
            _gameView.ReplayHandFinished -= OnReplayHandFinished;
            _gameView.ExitToMenuRequested -= OnReplayExitRequested;
        }
        _gameView = null;
        GameHost.Content = null;
        GameHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        _showingReplayHandResult = false;
        HandSaveRow.IsVisible = true;
        HandResultActionText.Text = "Next";
    }
}
