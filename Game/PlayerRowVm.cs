using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PokerApp;

public sealed class PlayerRowVm : INotifyPropertyChanged
{
    private int _chips;
    private Bitmap? _hole1;
    private Bitmap? _hole2;
    private bool _hideHoles = true;
    private bool _isCurrentTurn;
    private bool _isFoldedThisHand;
    private bool _isBusted;
    private bool _isReplayMode;
    private bool _isPromptPanelOpen;
    private bool _isThoughtPanelOpen;
    private string _lastAction = string.Empty;
    private string _lastPrompt = string.Empty;
    private string _lastThought = string.Empty;
    private IBrush _frameBrush = Brushes.Transparent;
    private double _seatOpacity = 1.0;

    public PlayerRowVm(string name) => Name = name;

    public string Name { get; }

    public int Chips
    {
        get => _chips;
        set => SetField(ref _chips, value);
    }

    public Bitmap? Hole1
    {
        get => _hole1;
        set => SetField(ref _hole1, value);
    }

    public Bitmap? Hole2
    {
        get => _hole2;
        set => SetField(ref _hole2, value);
    }

    public bool HideHoles
    {
        get => _hideHoles;
        set
        {
            if (!SetField(ref _hideHoles, value))
                return;
            OnPropertyChanged(nameof(ShowHoleBacks));
        }
    }

    public bool ShowHoleBacks => HideHoles;

    public bool IsCurrentTurn
    {
        get => _isCurrentTurn;
        set
        {
            if (!SetField(ref _isCurrentTurn, value))
                return;
            OnPropertyChanged(nameof(ShowLastAction));
            RecomputeVisuals();
        }
    }

    public bool IsFoldedThisHand
    {
        get => _isFoldedThisHand;
        set
        {
            if (!SetField(ref _isFoldedThisHand, value))
                return;
            RecomputeVisuals();
        }
    }

    public bool IsBusted
    {
        get => _isBusted;
        set
        {
            if (!SetField(ref _isBusted, value))
                return;
            RecomputeVisuals();
        }
    }

    public bool IsReplayMode
    {
        get => _isReplayMode;
        set => SetField(ref _isReplayMode, value);
    }

    public bool IsPromptPanelOpen
    {
        get => _isPromptPanelOpen;
        set => SetField(ref _isPromptPanelOpen, value);
    }

    public bool IsThoughtPanelOpen
    {
        get => _isThoughtPanelOpen;
        set => SetField(ref _isThoughtPanelOpen, value);
    }

    public string LastAction
    {
        get => _lastAction;
        set
        {
            if (!SetField(ref _lastAction, value))
                return;
            OnPropertyChanged(nameof(ShowLastAction));
        }
    }

    public bool ShowLastAction => !IsCurrentTurn && !string.IsNullOrWhiteSpace(LastAction);

    public string LastPrompt
    {
        get => _lastPrompt;
        set
        {
            if (!SetField(ref _lastPrompt, value))
                return;
            OnPropertyChanged(nameof(ShowLastPrompt));
        }
    }

    public string LastThought
    {
        get => _lastThought;
        set
        {
            if (!SetField(ref _lastThought, value))
                return;
            OnPropertyChanged(nameof(ShowLastThought));
        }
    }

    public bool ShowLastPrompt => !string.IsNullOrWhiteSpace(LastPrompt);

    public bool ShowLastThought => !string.IsNullOrWhiteSpace(LastThought);

    public IBrush FrameBrush
    {
        get => _frameBrush;
        private set => SetField(ref _frameBrush, value);
    }

    public double SeatOpacity
    {
        get => _seatOpacity;
        private set => SetField(ref _seatOpacity, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public void ResetForNewHand()
    {
        IsFoldedThisHand = false;
        LastAction = string.Empty;
        LastPrompt = string.Empty;
        LastThought = string.Empty;
        IsPromptPanelOpen = false;
        IsThoughtPanelOpen = false;
        IsCurrentTurn = false;
    }

    public void TogglePromptPanel()
    {
        IsPromptPanelOpen = !IsPromptPanelOpen;
        if (IsPromptPanelOpen)
            IsThoughtPanelOpen = false;
    }

    public void ToggleThoughtPanel()
    {
        IsThoughtPanelOpen = !IsThoughtPanelOpen;
        if (IsThoughtPanelOpen)
            IsPromptPanelOpen = false;
    }

    private void RecomputeVisuals()
    {
        if (IsCurrentTurn)
        {
            FrameBrush = Brushes.LimeGreen;
            SeatOpacity = 1.0;
            return;
        }

        FrameBrush = new SolidColorBrush(Color.Parse("#3c4048"));
        SeatOpacity = IsBusted || IsFoldedThisHand ? 0.45 : 1.0;
    }
}
