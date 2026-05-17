using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public partial class MainMenuWindow : Window
{
    private sealed class PersonalityPickItem
    {
        public int? Id { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Label { get; init; } = "";

        public override string ToString() => Label;
    }

    private sealed class PresetPickItem
    {
        public int? Id { get; init; }
        public string Name { get; init; } = "";
        public string ApiUrl { get; init; } = "";
        public string ApiKey { get; init; } = "";
        public string ModelName { get; init; } = "";
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
    private bool _abortingGame;
    private DispatcherTimer? _seriesSetupPrefsSaveDebounce;
    private bool _applyingSeriesSetupPrefs;
    private int? _activeSeriesId;

    public MainMenuWindow()
    {
        InitializeComponent();
        WindowState = WindowState.Maximized;
        BuyInTextBox.Text = GameConstants.DefaultBuyIn.ToString();
        SmallBlindTextBox.Text = GameConstants.DefaultSmallBlind.ToString();
        GameModeComboBox.SelectedIndex = 0;
        ReplaySourceComboBox.SelectedIndex = 0;
        FillBotCountComboItems(playMode: true);
        BotCountComboBox.SelectedIndex = 2;
        Bot1TypeComboBox.SelectedIndex = 0;
        Bot2TypeComboBox.SelectedIndex = 0;
        Bot3TypeComboBox.SelectedIndex = 0;
        Bot4TypeComboBox.SelectedIndex = 0;
        Bot5TypeComboBox.SelectedIndex = 0;
        Bot6TypeComboBox.SelectedIndex = 0;
        SetDefaultBotNames();
        SeriesFieldsPanel.IsVisible = false;
        RefreshBigBlindInfo();
        RefreshBotRows();
        RefreshPersonalityComboBoxes();
        RefreshPresetComboBoxes();
        LoadMenuBackground();
        HideSmallPotReplaysCheckBox.Content = $"Ukryj ręce z pulą max poniżej {GameConstants.HighPotChipsThreshold}";
        ExportIncludeHandHistoryCheckBox.IsChecked = true;
        WireSeriesSetupPreferencesAutosave();
        Closed += (_, _) => _menuBackgroundBitmap?.Dispose();
    }

    private bool IsSeriesSetupMode() => GameModeComboBox.SelectedIndex == 1;

    private void WireSeriesSetupPreferencesAutosave()
    {
        void onTextChanged(object? _, TextChangedEventArgs __) => ScheduleSaveSeriesSetupPreferences();

        SeriesNameTextBox.TextChanged += onTextChanged;
        SeriesTournamentCountTextBox.TextChanged += onTextChanged;
        BuyInTextBox.TextChanged += onTextChanged;

        for (var i = 0; i < 6; i++)
            BotNameTextBoxForIndex(i).TextChanged += onTextChanged;

        foreach (var c in AllPersonalityCombos())
            c.SelectionChanged += (_, _) => ScheduleSaveSeriesSetupPreferences();

        foreach (var c in AllPresetCombos())
            c.SelectionChanged += (_, _) => ScheduleSaveSeriesSetupPreferences();
    }

    private void ScheduleSaveSeriesSetupPreferences()
    {
        if (_applyingSeriesSetupPrefs || !IsSeriesSetupMode())
            return;

        if (_seriesSetupPrefsSaveDebounce is null)
        {
            _seriesSetupPrefsSaveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _seriesSetupPrefsSaveDebounce.Tick += OnSeriesSetupPrefsDebounceTick;
        }

        _seriesSetupPrefsSaveDebounce.Stop();
        _seriesSetupPrefsSaveDebounce.Start();
    }

    private async void OnSeriesSetupPrefsDebounceTick(object? sender, EventArgs e)
    {
        if (_seriesSetupPrefsSaveDebounce is null)
            return;
        _seriesSetupPrefsSaveDebounce.Stop();
        if (!IsSeriesSetupMode() || _applyingSeriesSetupPrefs)
            return;

        try
        {
            var payload = CaptureSeriesSetupPayloadFromUi();
            await TournamentSeriesSetupPreferencesStore.SaveAsync(payload);
        }
        catch
        {
        }
    }

    private TournamentSeriesSetupOptionsPayload CaptureSeriesSetupPayloadFromUi()
    {
        var bots = new List<TournamentSeriesSetupBotPayload>();
        for (var i = 0; i < 6; i++)
        {
            var type = GetBotTypeForIndex(i);
            var name = BotNameTextBoxForIndex(i).Text?.Trim() ?? "";
            int? personalityId = null;
            if (BotPersonalityComboForIndex(i).SelectedItem is PersonalityPickItem pp && pp.Id != null)
                personalityId = pp.Id;
            int? presetId = null;
            if (BotPresetComboForIndex(i).SelectedItem is PresetPickItem pr && pr.Id != null)
                presetId = pr.Id.Value;

            bots.Add(new TournamentSeriesSetupBotPayload
            {
                Type = type == BotType.LlmBotPlayer ? "LlmBotPlayer" : "RandomBotPlayer",
                Name = name,
                PersonalityId = personalityId,
                PresetId = presetId
            });
        }

        return new TournamentSeriesSetupOptionsPayload
        {
            SeriesName = SeriesNameTextBox.Text?.Trim() ?? "",
            TournamentCount = int.TryParse(SeriesTournamentCountTextBox.Text, out var tc) ? tc : 5,
            BuyIn = int.TryParse(BuyInTextBox.Text, out var bi) ? bi : GameConstants.DefaultBuyIn,
            SmallBlind = int.TryParse(SmallBlindTextBox.Text, out var sb) ? sb : GameConstants.DefaultSmallBlind,
            BotCount = GetSelectedBotCount(),
            Bots = bots
        };
    }

    private async void TryApplySeriesSetupPreferencesFromDb()
    {
        if (!IsSeriesSetupMode())
            return;

        TournamentSeriesSetupOptionsPayload? payload;
        try
        {
            payload = await TournamentSeriesSetupPreferencesStore.TryLoadAsync();
        }
        catch
        {
            return;
        }

        if (payload is null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _applyingSeriesSetupPrefs = true;
            try
            {
                SeriesNameTextBox.Text = payload.SeriesName;
                SeriesTournamentCountTextBox.Text = payload.TournamentCount.ToString();
                BuyInTextBox.Text = payload.BuyIn.ToString();
                SmallBlindTextBox.Text = payload.SmallBlind.ToString();
                RefreshBigBlindInfo();

                var botCount = Math.Clamp(payload.BotCount, 2, 6);
                var botIdx = botCount - 2;
                if (botIdx >= 0 && botIdx < BotCountComboBox.Items.Count)
                    BotCountComboBox.SelectedIndex = botIdx;

                RefreshBotRows();

                for (var i = 0; i < 6; i++)
                {
                    var slot = i < payload.Bots.Count ? payload.Bots[i] : null;
                    var typeCombo = BotTypeComboForIndex(i);
                    var wantLlm = string.Equals(slot?.Type, "LlmBotPlayer", StringComparison.Ordinal);
                    typeCombo.SelectedIndex = wantLlm ? 1 : 0;

                    var nameBox = BotNameTextBoxForIndex(i);
                    if (slot is not null && !string.IsNullOrWhiteSpace(slot.Name))
                        nameBox.Text = slot.Name;
                    else
                        SyncBotNameToType(nameBox, i, GetBotTypeForIndex(i));
                }

                RefreshPersonalityComboEnabledState();
                RefreshPresetComboEnabledState();

                for (var i = 0; i < 6; i++)
                {
                    var slot = i < payload.Bots.Count ? payload.Bots[i] : null;
                    SelectPersonalityById(BotPersonalityComboForIndex(i), slot?.PersonalityId);
                    SelectPresetById(BotPresetComboForIndex(i), slot?.PresetId);
                }
            }
            finally
            {
                _applyingSeriesSetupPrefs = false;
            }
        });
    }

    private static void SelectPersonalityById(ComboBox combo, int? personalityId)
    {
        if (personalityId is null)
        {
            combo.SelectedIndex = 0;
            return;
        }

        if (combo.ItemsSource is not IEnumerable<PersonalityPickItem> items)
        {
            combo.SelectedIndex = 0;
            return;
        }

        var idx = 0;
        foreach (var p in items)
        {
            if (p.Id == personalityId)
            {
                combo.SelectedIndex = idx;
                return;
            }

            idx++;
        }

        combo.SelectedIndex = 0;
    }

    private static void SelectPresetById(ComboBox combo, int? presetId)
    {
        if (presetId is null)
        {
            combo.SelectedIndex = 0;
            return;
        }

        if (combo.ItemsSource is not IEnumerable<PresetPickItem> items)
        {
            combo.SelectedIndex = 0;
            return;
        }

        var idx = 0;
        foreach (var p in items)
        {
            if (p.Id == presetId)
            {
                combo.SelectedIndex = idx;
                return;
            }

            idx++;
        }

        combo.SelectedIndex = 0;
    }

    private async void OnClearSeriesSetupPrefsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await TournamentSeriesSetupPreferencesStore.ClearAsync();
        }
        catch
        {
        }

        if (!IsSeriesSetupMode())
            return;

        _applyingSeriesSetupPrefs = true;
        try
        {
            SeriesNameTextBox.Text = "";
            SeriesTournamentCountTextBox.Text = "5";
            BuyInTextBox.Text = GameConstants.DefaultBuyIn.ToString();
            SmallBlindTextBox.Text = GameConstants.DefaultSmallBlind.ToString();
            RefreshBigBlindInfo();
            FillBotCountComboItems(playMode: false);
            BotCountComboBox.SelectedIndex = 2;
            RefreshBotRows();
            for (var i = 0; i < 6; i++)
            {
                BotTypeComboForIndex(i).SelectedIndex = 0;
                SyncBotNameToType(BotNameTextBoxForIndex(i), i, BotType.RandomBotPlayer);
            }

            RefreshPersonalityComboBoxes();
            RefreshPresetComboBoxes();
        }
        finally
        {
            _applyingSeriesSetupPrefs = false;
        }
    }

    private void FillBotCountComboItems(bool playMode)
    {
        BotCountComboBox.Items.Clear();
        if (playMode)
        {
            for (var i = 1; i <= 3; i++)
                BotCountComboBox.Items.Add(new ComboBoxItem { Content = i.ToString() });
        }
        else
        {
            for (var i = 2; i <= 6; i++)
                BotCountComboBox.Items.Add(new ComboBoxItem { Content = i.ToString() });
        }
    }

    private void OnGameModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var series = IsSeriesSetupMode();
        SeriesFieldsPanel.IsVisible = series;
        FillBotCountComboItems(playMode: !series);
        BotCountComboBox.SelectedIndex = series ? 4 : 2;
        RefreshBotRows();
        RefreshPersonalityComboBoxes();
        RefreshPresetComboBoxes();
        if (series)
            TryApplySeriesSetupPreferencesFromDb();
    }

    private void OnStartGamePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gameView is not null)
            return;
        SetupErrorTextBlock.Text = string.Empty;
        RefreshPersonalityComboBoxes();
        RefreshPresetComboBoxes();
        if (IsSeriesSetupMode())
            TryApplySeriesSetupPreferencesFromDb();
        SetOverlay(setupVisible: true, winnerVisible: false, handVisible: false);
    }

    private void OnExitPressed(object? sender, PointerPressedEventArgs e) => Close();

    private void OnPersonalitiesPressed(object? sender, PointerPressedEventArgs e)
    {
        PersonalitiesHost.Content = new PersonalitiesView { NavigateBack = LeavePersonalitiesView };
        MenuLayer.IsVisible = false;
        PersonalitiesHost.IsVisible = true;
    }

    private void OnPresetsPressed(object? sender, PointerPressedEventArgs e)
    {
        PresetsHost.Content = new PresetsView { NavigateBack = LeavePresetsView };
        MenuLayer.IsVisible = false;
        PresetsHost.IsVisible = true;
    }

    private async void OnViewReplayPressed(object? sender, PointerPressedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        ReplaySourceComboBox.SelectedIndex = 0;
        UpdateReplayPanelVisibility();
        await LoadActiveReplayPanelAsync();
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, replayListVisible: true);
    }

    private Task LoadActiveReplayPanelAsync() =>
        ReplaySourceComboBox.SelectedIndex <= 0
            ? LoadStandaloneReplaysAsync()
            : LoadSeriesListAsync();

    private void UpdateReplayPanelVisibility()
    {
        var standalone = ReplaySourceComboBox.SelectedIndex <= 0;
        StandaloneReplayDock.IsVisible = standalone;
        SeriesReplayRoot.IsVisible = !standalone;
        if (standalone)
            SeriesStatsTextBlock.Text = string.Empty;
    }

    private async void OnReplaySourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateReplayPanelVisibility();
        if (StandaloneReplayDock.IsVisible)
            await LoadStandaloneReplaysAsync();
        else
            await LoadSeriesListAsync();
    }

    private async void OnReplaySeriesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReplaySeriesListBox.SelectedItem is TournamentSeriesListItem sel)
        {
            await LoadSeriesHandsAsync(sel.Id);
            await RefreshSeriesStatsAsync(sel.Id);
        }
    }

    private async void OnHideSmallPotReplaysChanged(object? sender, RoutedEventArgs e)
    {
        if (StandaloneReplayDock.IsVisible)
            await LoadStandaloneReplaysAsync();
        else if (ReplaySeriesListBox.SelectedItem is TournamentSeriesListItem sel)
            await LoadSeriesHandsAsync(sel.Id);
        else
            await LoadSeriesListAsync();
    }

    private void OnCloseReplayListPressed(object? sender, PointerPressedEventArgs e)
    {
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, replayListVisible: false);
    }

    private async void OnOpenReplayPressed(object? sender, PointerPressedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        int? handId = null;
        if (StandaloneReplayDock.IsVisible)
        {
            if (ReplayHandsListBox.SelectedItem is StandaloneReplayListItem s)
                handId = s.Id;
        }
        else if (ReplaySeriesHandsListBox.SelectedItem is SeriesHandReplayItem h)
        {
            handId = h.Id;
        }

        if (handId is null)
        {
            ReplayErrorTextBlock.Text = "Wybierz powtórkę.";
            return;
        }

        string json;
        try
        {
            json = await HandPersistence.LoadHandHistoryJsonAsync(handId.Value);
        }
        catch
        {
            ReplayErrorTextBlock.Text = "Nie udało się wczytać powtórki.";
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            ReplayErrorTextBlock.Text = "Powtórka jest pusta.";
            return;
        }

        _gameView = BuildReplayView(json);
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

    private void LeavePresetsView()
    {
        PresetsHost.Content = null;
        PresetsHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        RefreshPresetComboBoxes();
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
            SetupErrorTextBlock.Text = "Buy-in musi być dodatni.";
            return;
        }

        if (!int.TryParse(SmallBlindTextBox.Text, out var smallBlind) || smallBlind <= 0)
        {
            SetupErrorTextBlock.Text = "Mały blind musi być dodatni.";
            return;
        }

        if (smallBlind * 20 > buyIn)
        {
            SetupErrorTextBlock.Text = "Mały blind min. 20x mniejszy od buy-in.";
            return;
        }

        var seriesMode = IsSeriesSetupMode();
        var botCount = GetSelectedBotCount();
        if (seriesMode)
        {
            if (botCount < 2 || botCount > 6)
            {
                SetupErrorTextBlock.Text = "Wybierz 2–6 botów.";
                return;
            }
        }
        else
        {
            if (botCount < 1 || botCount > 3)
            {
                SetupErrorTextBlock.Text = "Wybierz 1–3 botów.";
                return;
            }
        }

        var bots = new List<BotSetup>();
        for (var i = 0; i < botCount; i++)
        {
            var botType = GetBotTypeForIndex(i);
            var botName = GetBotNameForIndex(i, botType);
            if (string.IsNullOrWhiteSpace(botName))
            {
                SetupErrorTextBlock.Text = $"Bot {i + 1}: podaj nazwę.";
                return;
            }

            var personality = GetLlmPersonalitySnapshotForBotIndex(i);
            var preset = GetOpenAiPresetSnapshotForBotIndex(i);
            if (botType == BotType.LlmBotPlayer && preset is null)
            {
                SetupErrorTextBlock.Text = $"Bot {i + 1}: wybierz preset OpenAI.";
                return;
            }

            bots.Add(new BotSetup(botName, botType, personality, preset));
        }

        if (seriesMode)
        {
            var sname = SeriesNameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sname))
            {
                SetupErrorTextBlock.Text = "Podaj nazwę serii.";
                return;
            }

            if (!int.TryParse(SeriesTournamentCountTextBox.Text, out var tCount) || tCount < 1)
            {
                SetupErrorTextBlock.Text = "Min. 1 turniej w serii.";
                return;
            }

            _abortingGame = false;
            _gameCts = new CancellationTokenSource();
            SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false);
            try
            {
                await RunSeriesGameLoopAsync(buyIn, smallBlind, bots, sname, tCount, _gameCts.Token);
            }
            catch (OperationCanceledException)
            {
                FinishAbortedSeriesToMenu();
            }
            finally
            {
                _gameCts?.Dispose();
                _gameCts = null;
            }

            return;
        }

        var config = new GameSetupConfig(buyIn, smallBlind, bots, LlmTemperature: 0, SpectatorSeriesMode: false);

        _activeGameConfig = config;
        _abortingGame = false;
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

    private async Task RunSeriesGameLoopAsync(
        int buyIn,
        int smallBlind,
        List<BotSetup> botsTemplate,
        string seriesName,
        int tournamentCount,
        CancellationToken ct)
    {
        PokerDbBootstrap.EnsureInitialized();
        int seriesId;
        await using (var db = PokerDbBootstrap.CreateContext())
        {
            var series = new TournamentSeries { Name = seriesName, CreatedAt = DateTimeOffset.UtcNow };
            db.TournamentSeries.Add(series);
            await db.SaveChangesAsync(ct);
            seriesId = series.Id;
        }

        _activeSeriesId = seriesId;
        var seriesCancelled = false;
        try
        {
        for (var t = 0; t < tournamentCount; t++)
        {
            ct.ThrowIfCancellationRequested();
            int stId;
            await using (var db = PokerDbBootstrap.CreateContext())
            {
                var st = new SeriesTournament { TournamentSeriesId = seriesId, TournamentIndex = t };
                db.SeriesTournaments.Add(st);
                await db.SaveChangesAsync(ct);
                stId = st.Id;
            }

            var shuffled = botsTemplate.OrderBy(_ => Random.Shared.Next()).ToList();
            var cfg = new GameSetupConfig(
                buyIn,
                smallBlind,
                shuffled,
                LlmTemperature: 0,
                SpectatorSeriesMode: true,
                SeriesTournamentNumber: t + 1,
                SeriesTournamentTotal: tournamentCount);
            _activeGameConfig = cfg;
            var tmIdx = t;
            _gameView = new MainWindow(cfg);
            _gameView.TournamentFinished += OnSeriesInnerTournamentFinished;
            _gameView.ExitToMenuRequested += OnGameExitToMenuRequested;
            _gameView.HandFinishedAsync = (sum, json, tok) => SeriesHandFinishedAsync(sum, json, seriesId, stId, tmIdx, cfg, tok);
            GameHost.Content = _gameView;
            MenuLayer.IsVisible = false;
            GameHost.IsVisible = true;

            try
            {
                await _gameView.StartGameLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await HandPersistence.DiscardSeriesTournamentAsync(stId, CancellationToken.None);
                seriesCancelled = true;
                throw;
            }
            finally
            {
                if (_gameView is not null)
                {
                    _gameView.TournamentFinished -= OnSeriesInnerTournamentFinished;
                    _gameView.ExitToMenuRequested -= OnGameExitToMenuRequested;
                }

                _gameView = null;
                GameHost.Content = null;
            }
        }

        GameHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        _activeGameConfig = null;
        await TournamentSeriesStats.RecomputeAndSaveAsync(seriesId, ct);
        }
        catch (OperationCanceledException)
        {
            seriesCancelled = true;
            throw;
        }
        finally
        {
            if (seriesCancelled)
            {
                try
                {
                    await TournamentSeriesStats.RecomputeAndSaveAsync(seriesId, CancellationToken.None);
                }
                catch
                {
                }
            }

            _activeSeriesId = null;
        }
    }

    private void OnSeriesInnerTournamentFinished(string _) { }

    private static Task SeriesHandFinishedAsync(
        HandSummary summary,
        string json,
        int seriesId,
        int stId,
        int tournamentIndexZeroBased,
        GameSetupConfig cfg,
        CancellationToken ct)
    {
        var handName = $"Turniej {tournamentIndexZeroBased + 1}, Rozdanie {summary.HandNumber}";
        var seats = BuildSeatRowsForSave(cfg);
        return HandPersistence.SaveSeriesHandAsync(handName, json, seats, seriesId, stId, ct);
    }

    private void FinishAbortedSeriesToMenu()
    {
        if (_gameView is not null)
        {
            _gameView.TournamentFinished -= OnSeriesInnerTournamentFinished;
            _gameView.TournamentFinished -= OnTournamentFinished;
            _gameView.ExitToMenuRequested -= OnGameExitToMenuRequested;
        }

        _gameView = null;
        GameHost.Content = null;
        GameHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        SetOverlay(setupVisible: false, winnerVisible: false, handVisible: false, exitConfirmVisible: false);
        _activeGameConfig = null;
        _activeSeriesId = null;
        _pendingReplayJson = null;
        _showingReplayHandResult = false;
        _abortingGame = false;
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
        _abortingGame = true;
        _handContinueTcs?.TrySetResult(true);
        _handContinueTcs = null;
        _gameCts?.Cancel();
        if (_activeSeriesId.HasValue)
            FinishAbortedSeriesToMenu();
        else
            FinishAbortedGameToMenu();
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
        _abortingGame = false;
    }

    private void OnTournamentFinished(string winnerName)
    {
        WinnerText.Text = $"Zwycięzca: {winnerName}";
        SetOverlay(setupVisible: false, winnerVisible: true, handVisible: false);
    }

    private void OnBackToMenuPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gameView is not null)
        {
            _gameView.TournamentFinished -= OnTournamentFinished;
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

    private void OnSmallBlindChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshBigBlindInfo();
        ScheduleSaveSeriesSetupPreferences();
    }

    private void OnBotCountChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshBotRows();
        if (_applyingSeriesSetupPrefs)
        {
            RefreshPersonalityComboEnabledState();
            RefreshPresetComboEnabledState();
            return;
        }

        RefreshPersonalityComboBoxes();
        RefreshPresetComboBoxes();
        ScheduleSaveSeriesSetupPreferences();
    }

    private void OnBotTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingSeriesSetupPrefs)
        {
            RefreshPersonalityComboEnabledState();
            RefreshPresetComboEnabledState();
            return;
        }

        SyncDefaultBotNamesToTypes();
        RefreshPersonalityComboEnabledState();
        RefreshPresetComboEnabledState();
        ScheduleSaveSeriesSetupPreferences();
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
            BigBlindInfoText.Text = $"{smallBlind * 2} (zawsze x2 mały blind)";
        else
            BigBlindInfoText.Text = "Błąd małego blinda";
    }

    private void RefreshBotRows()
    {
        var count = GetSelectedBotCount();
        var series = IsSeriesSetupMode();
        Bot1Row.IsVisible = count >= 1;
        Bot2Row.IsVisible = count >= 2;
        Bot3Row.IsVisible = count >= 3;
        Bot4Row.IsVisible = series && count >= 4;
        Bot5Row.IsVisible = series && count >= 5;
        Bot6Row.IsVisible = series && count >= 6;
        RefreshPersonalityComboEnabledState();
        RefreshPresetComboEnabledState();
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
        foreach (var c in AllPersonalityCombos())
        {
            c.ItemsSource = arr;
            c.SelectedIndex = 0;
        }

        RefreshPersonalityComboEnabledState();
    }

    private void RefreshPresetComboBoxes()
    {
        var items = new List<PresetPickItem>
        {
            new() { Label = "(None)" }
        };
        try
        {
            PokerDbBootstrap.EnsureInitialized();
            using var db = PokerDbBootstrap.CreateContext();
            foreach (var p in db.OpenAiPresets.AsNoTracking().OrderBy(x => x.Name))
            {
                items.Add(new PresetPickItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    ApiUrl = p.ApiUrl,
                    ApiKey = p.ApiKey,
                    ModelName = p.ModelName,
                    Label = p.Name
                });
            }
        }
        catch
        {
        }

        var arr = items.ToArray();
        foreach (var c in AllPresetCombos())
        {
            c.ItemsSource = arr;
            c.SelectedIndex = 0;
        }

        RefreshPresetComboEnabledState();
    }

    private IEnumerable<ComboBox> AllPersonalityCombos()
    {
        yield return Bot1PersonalityComboBox;
        yield return Bot2PersonalityComboBox;
        yield return Bot3PersonalityComboBox;
        yield return Bot4PersonalityComboBox;
        yield return Bot5PersonalityComboBox;
        yield return Bot6PersonalityComboBox;
    }

    private IEnumerable<ComboBox> AllPresetCombos()
    {
        yield return Bot1PresetComboBox;
        yield return Bot2PresetComboBox;
        yield return Bot3PresetComboBox;
        yield return Bot4PresetComboBox;
        yield return Bot5PresetComboBox;
        yield return Bot6PresetComboBox;
    }

    private void RefreshPersonalityComboEnabledState()
    {
        var count = GetSelectedBotCount();
        for (var i = 0; i < 6; i++)
            BotPersonalityComboForIndex(i).IsEnabled = i < count && GetBotTypeForIndex(i) == BotType.LlmBotPlayer;
    }

    private void RefreshPresetComboEnabledState()
    {
        var count = GetSelectedBotCount();
        for (var i = 0; i < 6; i++)
            BotPresetComboForIndex(i).IsEnabled = i < count && GetBotTypeForIndex(i) == BotType.LlmBotPlayer;
    }

    private async Task OnHandFinishedAsync(HandSummary summary, string replayJson, CancellationToken cancellationToken)
    {
        if (_abortingGame)
            return;
        _showingReplayHandResult = false;
        var winners = string.Join(", ", summary.Winners);
        HandWinnerText.Text = $"Zwycięzca: {winners}";
        HandResultActionText.Text = "Dalej";
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
            HandSaveErrorTextBlock.Text = "Podaj nazwę rozdania.";
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

    private static List<(string Name, string PlayerType, int? LlmPersonalityId, int? OpenAiPresetId)> BuildSeatRowsForSave(GameSetupConfig config)
    {
        var list = new List<(string, string, int?, int?)>();
        if (!config.SpectatorSeriesMode)
            list.Add((GameConstants.HumanPlayerName, "HUMAN", null, null));
        foreach (var b in config.Bots)
        {
            var t = b.Type == BotType.LlmBotPlayer ? "LLM_AGENT" : "RANDOM_BOT";
            list.Add((b.Name, t, b.LlmPersonality?.Id, b.OpenAiPreset?.Id));
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
            3 => Bot4PersonalityComboBox,
            4 => Bot5PersonalityComboBox,
            5 => Bot6PersonalityComboBox,
            _ => Bot1PersonalityComboBox
        };

    private ComboBox BotPresetComboForIndex(int botIndex) =>
        botIndex switch
        {
            0 => Bot1PresetComboBox,
            1 => Bot2PresetComboBox,
            2 => Bot3PresetComboBox,
            3 => Bot4PresetComboBox,
            4 => Bot5PresetComboBox,
            5 => Bot6PresetComboBox,
            _ => Bot1PresetComboBox
        };

    private ComboBox BotTypeComboForIndex(int botIndex) =>
        botIndex switch
        {
            0 => Bot1TypeComboBox,
            1 => Bot2TypeComboBox,
            2 => Bot3TypeComboBox,
            3 => Bot4TypeComboBox,
            4 => Bot5TypeComboBox,
            5 => Bot6TypeComboBox,
            _ => Bot1TypeComboBox
        };

    private TextBox BotNameTextBoxForIndex(int botIndex) =>
        botIndex switch
        {
            0 => Bot1NameTextBox,
            1 => Bot2NameTextBox,
            2 => Bot3NameTextBox,
            3 => Bot4NameTextBox,
            4 => Bot5NameTextBox,
            5 => Bot6NameTextBox,
            _ => Bot1NameTextBox
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

    private OpenAiPresetSnapshot? GetOpenAiPresetSnapshotForBotIndex(int botIndex)
    {
        if (GetBotTypeForIndex(botIndex) != BotType.LlmBotPlayer)
            return null;
        var combo = BotPresetComboForIndex(botIndex);
        if (combo.SelectedItem is PresetPickItem pick && pick.Id != null)
            return new OpenAiPresetSnapshot(pick.Id.Value, pick.Name, pick.ApiUrl, pick.ApiKey, pick.ModelName);
        return null;
    }

    private BotType GetBotTypeForIndex(int botIndex) =>
        BotTypeComboForIndex(botIndex).SelectedIndex == 1 ? BotType.LlmBotPlayer : BotType.RandomBotPlayer;

    private string GetBotNameForIndex(int botIndex, BotType type)
    {
        var textBox = BotNameTextBoxForIndex(botIndex);
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
        for (var i = 0; i < 6; i++)
            SyncBotNameToType(BotNameTextBoxForIndex(i), i, GetBotTypeForIndex(i));
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

    private async Task LoadStandaloneReplaysAsync()
    {
        PokerDbBootstrap.EnsureInitialized();
        var hide = HideSmallPotReplaysCheckBox.IsChecked == true;
        await using var db = PokerDbBootstrap.CreateContext();
        var query = db.SavedHands
            .AsNoTracking()
            .Where(h => h.TournamentSeriesId == null);
        if (hide)
            query = query.Where(h => h.MaxPot >= GameConstants.HighPotChipsThreshold);
        var raw = await query
            .OrderByDescending(h => h.HandTimeIso)
            .Select(h => new { h.Id, h.HandName, h.HandTimeIso, h.MaxPot })
            .ToListAsync();
        var items = raw.Select(h =>
        {
            var bg = h.MaxPot >= GameConstants.HighPotChipsThreshold ? "#2a4a32" : "#1a2230";
            return new StandaloneReplayListItem
            {
                Id = h.Id,
                Name = h.HandName,
                DisplayLabel = $"{FormatReplayTime(h.HandTimeIso)} — {h.HandName} · max pula {h.MaxPot}",
                MaxPot = h.MaxPot,
                RowBackground = bg
            };
        }).ToList();

        ReplayHandsListBox.ItemsSource = items;
        ReplayHandsListBox.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    private async Task LoadSeriesListAsync()
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var series = (await db.TournamentSeries
            .AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.CreatedAt })
            .ToListAsync())
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        var items = series.Select(s => new TournamentSeriesListItem
        {
            Id = s.Id,
            SeriesName = s.Name,
            DisplayLabel = $"{FormatReplayTime(s.CreatedAt.ToString("O"))} — {s.Name}"
        }).ToList();
        ReplaySeriesListBox.ItemsSource = items;
        ReplaySeriesListBox.SelectedIndex = items.Count > 0 ? 0 : -1;
        if (items.Count > 0)
        {
            await LoadSeriesHandsAsync(items[0].Id);
            await RefreshSeriesStatsAsync(items[0].Id);
        }
        else
        {
            ReplaySeriesHandsListBox.ItemsSource = Array.Empty<SeriesHandReplayItem>();
            SeriesStatsTextBlock.Text = string.Empty;
        }
    }

    private async Task RefreshSeriesStatsAsync(int seriesId)
    {
        PokerDbBootstrap.EnsureInitialized();
        await using var db = PokerDbBootstrap.CreateContext();
        var s = await db.TournamentSeries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == seriesId);
        SeriesStatsTextBlock.Text = s is null
            ? string.Empty
            : TournamentSeriesStats.FormatStatsForDisplay(s);
    }

    private static string SanitizeSeriesFileStem(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        var t = name.Trim();
        return string.IsNullOrEmpty(t) ? "series" : t;
    }

    private async void OnExportSeriesCsvClick(object? sender, RoutedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        if (ReplaySeriesListBox.SelectedItem is not TournamentSeriesListItem sel)
        {
            ReplayErrorTextBlock.Text = "Wybierz serię turniejów.";
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        var includeHistory = ExportIncludeHandHistoryCheckBox.IsChecked == true;
        var stem = SanitizeSeriesFileStem(sel.SeriesName);
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Eksport serii turniejów",
            SuggestedFileName = $"{stem}_details.csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV")
                {
                    Patterns = ["*.csv"]
                }
            ]
        });
        if (file is null)
            return;

        try
        {
            var csv = await SeriesDetailsCsvExporter.BuildCsvAsync(sel.Id, includeHistory);
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(csv);
        }
        catch
        {
            ReplayErrorTextBlock.Text = "Eksport nieudany.";
        }
    }

    private async void OnDeleteSeriesClick(object? sender, RoutedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        if (ReplaySeriesListBox.SelectedItem is not TournamentSeriesListItem sel)
        {
            ReplayErrorTextBlock.Text = "Wybierz serię turniejów.";
            return;
        }

        try
        {
            await HandPersistence.DeleteTournamentSeriesAsync(sel.Id);
            SeriesStatsTextBlock.Text = string.Empty;
            await LoadSeriesListAsync();
        }
        catch
        {
            ReplayErrorTextBlock.Text = "Nie udało się usunąć serii.";
        }
    }

    private async Task LoadSeriesHandsAsync(int seriesId)
    {
        var hide = HideSmallPotReplaysCheckBox.IsChecked == true;
        await using var db = PokerDbBootstrap.CreateContext();
        var query = db.SavedHands
            .AsNoTracking()
            .Where(h => h.TournamentSeriesId == seriesId);
        if (hide)
            query = query.Where(h => h.MaxPot >= GameConstants.HighPotChipsThreshold);
        var raw = await query
            .OrderByDescending(h => h.HandTimeIso)
            .Select(h => new { h.Id, h.HandName, h.HandTimeIso, h.MaxPot })
            .ToListAsync();
        var items = raw.Select(h =>
        {
            var bg = h.MaxPot >= GameConstants.HighPotChipsThreshold ? "#2a4a32" : "#1a2230";
            return new SeriesHandReplayItem
            {
                Id = h.Id,
                Name = h.HandName,
                DisplayLabel = $"{FormatReplayTime(h.HandTimeIso)} — {h.HandName} · max pula {h.MaxPot}",
                MaxPot = h.MaxPot,
                RowBackground = bg
            };
        }).ToList();

        ReplaySeriesHandsListBox.ItemsSource = items;
        ReplaySeriesHandsListBox.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    private async void OnDeleteStandaloneReplayClick(object? sender, RoutedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        if (sender is not Control { DataContext: StandaloneReplayListItem item })
        {
            ReplayErrorTextBlock.Text = "Nie można ustalić powtórki.";
            return;
        }

        try
        {
            PokerDbBootstrap.EnsureInitialized();
            await using var db = PokerDbBootstrap.CreateContext();
            var hand = await db.SavedHands.FirstOrDefaultAsync(h => h.Id == item.Id);
            if (hand is null)
            {
                ReplayErrorTextBlock.Text = "Powtórka już nie istnieje.";
                await LoadStandaloneReplaysAsync();
                return;
            }
            db.SavedHands.Remove(hand);
            await db.SaveChangesAsync();
            await LoadStandaloneReplaysAsync();
        }
        catch
        {
            ReplayErrorTextBlock.Text = "Nie udało się usunąć powtórki.";
        }
    }

    private async void OnDeleteSeriesHandReplayClick(object? sender, RoutedEventArgs e)
    {
        ReplayErrorTextBlock.Text = string.Empty;
        if (sender is not Control { DataContext: SeriesHandReplayItem item })
        {
            ReplayErrorTextBlock.Text = "Nie można ustalić powtórki.";
            return;
        }

        var seriesId = ReplaySeriesListBox.SelectedItem is TournamentSeriesListItem s ? s.Id : 0;
        try
        {
            PokerDbBootstrap.EnsureInitialized();
            await using var db = PokerDbBootstrap.CreateContext();
            var hand = await db.SavedHands.FirstOrDefaultAsync(h => h.Id == item.Id);
            if (hand is null)
            {
                ReplayErrorTextBlock.Text = "Powtórka już nie istnieje.";
                if (seriesId != 0)
                    await LoadSeriesHandsAsync(seriesId);
                return;
            }
            db.SavedHands.Remove(hand);
            await db.SaveChangesAsync();
            if (seriesId != 0)
                await LoadSeriesHandsAsync(seriesId);
        }
        catch
        {
            ReplayErrorTextBlock.Text = "Nie udało się usunąć powtórki.";
        }
    }

    private static string FormatReplayTime(string handTimeIso)
    {
        if (DateTimeOffset.TryParse(handTimeIso, out var dto))
            return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return handTimeIso;
    }

    private MainWindow BuildReplayView(string replayJson)
    {
        var defaultBuyIn = GameConstants.DefaultBuyIn;
        var defaultSmallBlind = GameConstants.DefaultSmallBlind;
        var bots = new List<BotSetup>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(replayJson);
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

        var config = new GameSetupConfig(
            defaultBuyIn,
            defaultSmallBlind,
            bots,
            SpectatorSeriesMode: !ReplayIncludesHumanPlayer(replayJson));
        var view = new MainWindow(config, isReplayMode: true);
        view.ExitToMenuRequested += OnReplayExitRequested;
        view.LoadReplay(replayJson);
        return view;
    }

    private static bool ReplayIncludesHumanPlayer(string replayJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(replayJson);
            if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != System.Text.Json.JsonValueKind.Array)
                return true;
            foreach (var ev in events.EnumerateArray())
            {
                if (!ev.TryGetProperty("ev", out var evType) || evType.GetString() != "replay_header")
                    continue;
                if (!ev.TryGetProperty("players", out var players) || players.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return true;
                foreach (var p in players.EnumerateArray())
                {
                    var typeText = p.TryGetProperty("player_type", out var pt) ? pt.GetString() ?? "" : "";
                    if (string.Equals(typeText, "HUMAN", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
        }
        catch
        {
        }

        return true;
    }

    private void OnReplayExitRequested()
    {
        if (_gameView is not null)
            _gameView.ExitToMenuRequested -= OnReplayExitRequested;
        _gameView = null;
        GameHost.Content = null;
        GameHost.IsVisible = false;
        MenuLayer.IsVisible = true;
        _showingReplayHandResult = false;
        HandSaveRow.IsVisible = true;
        HandResultActionText.Text = "Dalej";
    }
}
