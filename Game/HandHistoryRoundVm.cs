using System.Collections.ObjectModel;

namespace PokerApp;

public sealed class HandHistoryRoundVm
{
    public HandHistoryRoundVm(string roundName) => RoundName = roundName;

    public string RoundName { get; }

    public ObservableCollection<HandHistoryActionVm> Actions { get; } = new();
}
