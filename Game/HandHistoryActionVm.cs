using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PokerApp;

public sealed class HandHistoryActionVm : INotifyPropertyChanged
{
    private bool _isPromptExpanded;
    private bool _isThoughtExpanded;
    private bool _showReplayDiagnostics;

    public HandHistoryActionVm(string text, string promptText, string thoughtText, bool showReplayDiagnostics)
    {
        Text = text;
        PromptText = promptText;
        ThoughtText = thoughtText;
        _showReplayDiagnostics = showReplayDiagnostics;
    }

    public string Text { get; }

    public string PromptText { get; }

    public string ThoughtText { get; }

    public bool HasPrompt => !string.IsNullOrWhiteSpace(PromptText);

    public bool HasThought => !string.IsNullOrWhiteSpace(ThoughtText);

    public bool ShowReplayDiagnostics
    {
        get => _showReplayDiagnostics;
        set
        {
            if (!SetField(ref _showReplayDiagnostics, value))
                return;
            OnPropertyChanged(nameof(ShowPromptLink));
            OnPropertyChanged(nameof(ShowThoughtLink));
            OnPropertyChanged(nameof(ShowPromptPanel));
            OnPropertyChanged(nameof(ShowThoughtPanel));
        }
    }

    public bool ShowPromptLink => ShowReplayDiagnostics && HasPrompt;

    public bool ShowThoughtLink => ShowReplayDiagnostics && HasThought;

    public bool IsPromptExpanded
    {
        get => _isPromptExpanded;
        set
        {
            if (!SetField(ref _isPromptExpanded, value))
                return;
            OnPropertyChanged(nameof(ShowPromptPanel));
        }
    }

    public bool IsThoughtExpanded
    {
        get => _isThoughtExpanded;
        set
        {
            if (!SetField(ref _isThoughtExpanded, value))
                return;
            OnPropertyChanged(nameof(ShowThoughtPanel));
        }
    }

    public bool ShowPromptPanel => ShowReplayDiagnostics && IsPromptExpanded;

    public bool ShowThoughtPanel => ShowReplayDiagnostics && IsThoughtExpanded;

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
}
