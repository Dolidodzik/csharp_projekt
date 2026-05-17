using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Input;

namespace PokerApp;

public partial class MainWindow : UserControl
{
    private bool _loopStarted;
    public event Action<string>? TournamentFinished;
    public Func<HandSummary, string, CancellationToken, Task>? HandFinishedAsync;
    public event Action? ExitToMenuRequested;
    private readonly bool _isReplayMode;

    public MainWindow() : this(GameSetupConfig.CreateDefault())
    {
    }

    public MainWindow(GameSetupConfig config, bool isReplayMode = false)
    {
        InitializeComponent();
        var vm = new MainWindowViewModel(config);
        vm.HistoryUiChanged += OnHistoryUiChanged;
        _isReplayMode = isReplayMode;
        if (_isReplayMode)
            vm.InitializeReplay("{}");
        DataContext = vm;
    }

    public MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public async Task StartGameLoopAsync(CancellationToken cancellationToken = default)
    {
        if (_isReplayMode)
            return;
        if (_loopStarted)
            return;
        _loopStarted = true;

        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.AttachGameCancellation(cancellationToken);
        vm.InitializeTournament();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            vm.PrepareForNextHandVisuals();
            var summary = await vm.PlayNextHandAsync(cancellationToken);
            var replayJson = vm.BuildHandReplayJson(summary);
            if (HandFinishedAsync is not null)
                await HandFinishedAsync(summary, replayJson, cancellationToken);

            if (summary.TournamentFinished)
            {
                TournamentFinished?.Invoke(summary.TournamentWinner ?? "Unknown");
                return;
            }
        }
    }

    private void OnExitToMenuClick(object? sender, RoutedEventArgs e) => ExitToMenuRequested?.Invoke();

    private void OnFold(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.HumanSubmitFold();
    }

    private void OnCheckCall(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.HumanSubmitCheckCall();
    }

    private void OnRaise(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.HumanSubmitRaise();
    }

    private void OnHistoryPromptPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock tb || tb.DataContext is not HandHistoryActionVm item)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.ToggleHistoryActionPanel(item, showPrompt: true);
    }

    private void OnHistoryThoughtPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock tb || tb.DataContext is not HandHistoryActionVm item)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.ToggleHistoryActionPanel(item, showPrompt: false);
    }

    public void LoadReplay(string replayJson)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.InitializeReplay(replayJson);
    }

    private void OnNextReplayActionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.AdvanceReplay();
        }
    }

    private void OnHistoryUiChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ScrollViewer>("HistoryScrollViewer") is { } sv)
                sv.Offset = new Vector(sv.Offset.X, double.MaxValue);
        }, DispatcherPriority.Background);
    }
}
