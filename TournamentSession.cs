using System.Reflection;
using TexasHoldem.Logic.GameMechanics;
using TexasHoldem.Logic.Players;

namespace PokerApp;

public sealed record HandSummary(
    int HandNumber,
    IReadOnlyList<string> Winners,
    IReadOnlyDictionary<string, int> Stacks,
    bool TournamentFinished,
    string? TournamentWinner);

public sealed class TournamentSession
{
    private static readonly Assembly LogicAssembly = typeof(TexasHoldemGame).Assembly;
    private static readonly Type InternalPlayerType = LogicAssembly.GetType("TexasHoldem.Logic.GameMechanics.InternalPlayer")!;
    private static readonly Type HandLogicType = LogicAssembly.GetType("TexasHoldem.Logic.GameMechanics.HandLogic")!;
    private static readonly ConstructorInfo InternalCtor = InternalPlayerType.GetConstructor(new[] { typeof(IPlayer) })!;
    private static readonly ConstructorInfo HandCtor = HandLogicType
        .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(c => c.GetParameters() is { Length: 3 } p
            && p[0].ParameterType.IsGenericType
            && p[0].ParameterType.GetGenericTypeDefinition() == typeof(IList<>)
            && p[1].ParameterType == typeof(int)
            && p[2].ParameterType == typeof(int));
    private static readonly PropertyInfo PlayerDecoratorPlayer = typeof(PlayerDecorator)
        .GetProperty("Player", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo StartGameMethod = InternalPlayerType.GetMethod(
        nameof(IPlayer.StartGame),
        BindingFlags.Instance | BindingFlags.Public,
        null,
        new[] { typeof(IStartGameContext) },
        null)!;

    private readonly int _smallBlind;
    private readonly object _master;
    private object _shifted;
    private int _handNumber;
    private bool _endGameSent;

    public TournamentSession(IReadOnlyList<IPlayer> players, int startingStack, int smallBlind)
    {
        _smallBlind = smallBlind;
        _master = NewInternalList();
        foreach (var p in players)
            ListAdd(_master, InternalCtor.Invoke(new object[] { p }));

        var names = players.Select(p => p.Name).ToList().AsReadOnly();
        var count = ListCount(_master);
        for (var i = 0; i < count; i++)
        {
            var ip = ListGet(_master, i);
            var buyIn = GetWrappedPlayer(ip).BuyIn;
            var stack = buyIn == -1 ? startingStack : buyIn;
            StartGameMethod.Invoke(ip, new object[] { new StartGameContext(names, stack) });
        }

        _shifted = NewInternalList();
        for (var i = 0; i < count; i++)
            ListAdd(_shifted, ListGet(_master, i));
    }

    public bool IsFinished => AliveCount(_master) <= 1;

    public HandSummary PlayNextHand(CancellationToken cancellationToken)
    {
        if (IsFinished)
            throw new InvalidOperationException("Tournament already finished.");

        cancellationToken.ThrowIfCancellationRequested();
        var before = GetStacks();

        var alive = NewInternalList();
        var shiftedCount = ListCount(_shifted);
        for (var i = 0; i < shiftedCount; i++)
        {
            var ip = ListGet(_shifted, i);
            if (GetStack(ip) > 0)
                ListAdd(alive, ip);
        }

        if (ListCount(alive) <= 1)
            throw new InvalidOperationException("No active hand possible.");

        var button = ListGet(alive, 0);
        ListRemoveAt(alive, 0);
        ListAdd(alive, button);

        _handNumber++;
        var hand = HandCtor.Invoke(new[] { alive, _handNumber, _smallBlind });
        ((IHandLogic)hand).Play();
        _shifted = alive;

        var after = GetStacks();
        var winners = after
            .Where(kv => kv.Value > before.GetValueOrDefault(kv.Key))
            .OrderByDescending(kv => kv.Value - before.GetValueOrDefault(kv.Key))
            .Select(kv => kv.Key)
            .ToList();

        if (winners.Count == 0)
        {
            var maxStack = after.Values.Max();
            winners = after.Where(kv => kv.Value == maxStack).Select(kv => kv.Key).ToList();
        }

        var finished = IsFinished;
        string? tournamentWinner = null;
        if (finished)
        {
            tournamentWinner = after.FirstOrDefault(kv => kv.Value > 0).Key;
            if (!_endGameSent && !string.IsNullOrWhiteSpace(tournamentWinner))
            {
                var mc = ListCount(_master);
                for (var i = 0; i < mc; i++)
                    GetWrappedPlayer(ListGet(_master, i)).EndGame(new EndGameContext(tournamentWinner));
                _endGameSent = true;
            }
        }

        return new HandSummary(_handNumber, winners, after, finished, tournamentWinner);
    }

    private Dictionary<string, int> GetStacks()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var c = ListCount(_master);
        for (var i = 0; i < c; i++)
        {
            var ip = ListGet(_master, i);
            result[GetWrappedPlayer(ip).Name] = GetStack(ip);
        }

        return result;
    }

    private static int AliveCount(object list)
    {
        var n = 0;
        var c = ListCount(list);
        for (var i = 0; i < c; i++)
        {
            if (GetStack(ListGet(list, i)) > 0)
                n++;
        }

        return n;
    }

    private static int GetStack(object internalPlayer)
    {
        var pm = internalPlayer.GetType().GetProperty("PlayerMoney")!.GetValue(internalPlayer)!;
        return (int)pm.GetType().GetProperty("Money")!.GetValue(pm)!;
    }

    private static IPlayer GetWrappedPlayer(object internalPlayer) =>
        (IPlayer)PlayerDecoratorPlayer.GetValue(internalPlayer)!;

    private static object NewInternalList()
    {
        var listType = typeof(List<>).MakeGenericType(InternalPlayerType);
        return Activator.CreateInstance(listType)!;
    }

    private static void ListAdd(object list, object item) =>
        list.GetType().GetMethod("Add")!.Invoke(list, new[] { item });

    private static int ListCount(object list) =>
        (int)list.GetType().GetProperty("Count")!.GetValue(list)!;

    private static object ListGet(object list, int index) =>
        list.GetType().GetProperty("Item")!.GetValue(list, new object[] { index })!;

    private static void ListRemoveAt(object list, int index) =>
        list.GetType().GetMethod("RemoveAt", new[] { typeof(int) })!.Invoke(list, new object[] { index });
}
