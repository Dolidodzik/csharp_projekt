using System.Reflection;
using TexasHoldem.Logic.GameMechanics;
using TexasHoldem.Logic.Players;

namespace PokerApp;

/// <summary>wynik jednej rozegranej ręki — przekazywany do zapisu i overlayu końca rozdania.</summary>
/// <param name="HandNumber">która ręka w turnieju.</param>
/// <param name="Winners">kto zgarnął pulę (może być remis).</param>
/// <param name="Stacks">stacki wszystkich graczy po rozdaniu.</param>
/// <param name="TournamentFinished">true gdy został jeden gracz z żetonami.</param>
/// <param name="TournamentWinner">nazwa zwycięzcy turnieju, null jeśli turniej trwa.</param>
public sealed record HandSummary(
    int HandNumber,
    IReadOnlyList<string> Winners,
    IReadOnlyList<(string Name, int Stack)> Stacks,
    bool TournamentFinished,
    string? TournamentWinner);

/// <summary>
/// opakowuje TexasHoldemGameEngine w tryb turniejowy — wiele rąk, rotacja buttona, blindy.
/// </summary>
/// <remarks>
/// pakiet NuGet nie wystawia publicznego API turniejowego (InternalPlayer, HandLogic są internal),
/// więc cała klasa opiera się na refleksji. to świadomy kompromis: aktualizacja silnika może wymagać poprawek tutaj,
/// ale nie przepisujemy zasad Hold'em od zera.
/// </remarks>
/// <seealso cref="TournamentBlindSchedule"/>
/// <seealso cref="MainWindowViewModel.InitializeTournament"/>
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

    private readonly int _baseSmallBlind;
    private readonly bool _escalatingBlinds;
    private readonly object _master;
    private object _shifted;
    private int _handNumber;
    private bool _endGameSent;

    /// <value>big blind bieżącej ręki — rośnie w serii turniejów (escalatingBlinds).</value>
    public int CurrentHandBigBlind { get; private set; }

    /// <value>numer następnej ręki — używany w UI przed startem rozdania.</value>
    public int UpcomingHandNumber => _handNumber + 1;

    /// <summary>
    /// startuje turniej: opakowuje każdego <see cref="IPlayer"/> w InternalPlayer i woła StartGame.
    /// </summary>
    /// <param name="players">kolejność miejsc przy stole — nie zmienia się w trakcie turnieju.</param>
    /// <param name="startingStack">buy-in, jeśli gracz ma BuyIn == -1.</param>
    /// <param name="baseSmallBlind">mała ciemna z konfiguracji stołu.</param>
    /// <param name="escalatingBlinds">true w trybie serii botów — blindy rosną co N rąk.</param>
    public TournamentSession(IReadOnlyList<IPlayer> players, int startingStack, int baseSmallBlind, bool escalatingBlinds = false)
    {
        _baseSmallBlind = baseSmallBlind;
        _escalatingBlinds = escalatingBlinds;
        CurrentHandBigBlind = TournamentBlindSchedule.SmallBlindForHand(baseSmallBlind, 1, escalatingBlinds) * 2;
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

    /// <value>true gdy co najwyżej jeden gracz ma stack &gt; 0.</value>
    public bool IsFinished => AliveCount(_master) <= 1;

    /// <summary>
    /// rozgrywa jedną rękę: filtruje busted graczy, przesuwa buttona, liczy blindy, woła HandLogic.Play().
    /// </summary>
    /// <remarks>
    /// wywoływane z wątku puli przez <see cref="MainWindowViewModel.PlayNextHandAsync"/> —
    /// callbacki graczy muszą same zsynchronizować UI.
    /// </remarks>
    /// <param name="cancellationToken">anulowanie serii / wyjście z gry.</param>
    /// <returns>podsumowanie ręki i ewentualny koniec turnieju.</returns>
    /// <exception cref="InvalidOperationException">gdy turniej skończony albo nikt nie może grać.</exception>
    public HandSummary PlayNextHand(CancellationToken cancellationToken)
    {
        if (IsFinished)
            throw new InvalidOperationException("Tournament already finished.");

        cancellationToken.ThrowIfCancellationRequested();
        var before = GetStacksList();

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
        var smallThisHand = TournamentBlindSchedule.SmallBlindForHand(_baseSmallBlind, _handNumber, _escalatingBlinds);
        CurrentHandBigBlind = smallThisHand * 2;
        var hand = HandCtor.Invoke(new[] { alive, _handNumber, smallThisHand });
        ((IHandLogic)hand).Play();
        _shifted = alive;

        var after = GetStacksList();
        var gains = new List<(int Index, string Name, int Gain)>();
        for (var i = 0; i < after.Count; i++)
        {
            var g = after[i].Stack - before[i].Stack;
            if (g > 0)
                gains.Add((i, after[i].Name, g));
        }

        var winners = gains
            .OrderByDescending(x => x.Gain)
            .ThenBy(x => x.Index)
            .Select(x => x.Name)
            .ToList();

        if (winners.Count == 0)
        {
            var maxStack = after.Max(x => x.Stack);
            winners = after.Where(x => x.Stack == maxStack).Select(x => x.Name).ToList();
        }

        var finished = IsFinished;
        string? tournamentWinner = null;
        if (finished)
        {
            foreach (var x in after)
            {
                if (x.Stack > 0)
                {
                    tournamentWinner = x.Name;
                    break;
                }
            }

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

    private List<(string Name, int Stack)> GetStacksList()
    {
        var c = ListCount(_master);
        var list = new List<(string, int)>(c);
        for (var i = 0; i < c; i++)
        {
            var ip = ListGet(_master, i);
            list.Add((GetWrappedPlayer(ip).Name, GetStack(ip)));
        }

        return list;
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
