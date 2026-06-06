using System.Text;
using System.Text.Json;

namespace PokerApp;

/// <summary>
/// helpery do JSON powtórek — parsowanie bez pełnego modelu DTO, bo schema eventów jest luźna.
/// </summary>
/// <remarks>
/// wszystkie metody łapią wyjątki i zwracają bezpieczne domyślne — stara baza lub ręcznie edytowany JSON
/// nie powinien wywalić listy powtórek.
/// </remarks>
/// <seealso cref="HandPersistence"/>
/// <seealso cref="TournamentSeriesStats"/>
public static class ReplayJsonUtil
{
    /// <summary>
    /// szuka max pot w zdarzeniach action — zapisujemy przy Save, żeby filtrować listę bez deserializacji całości.
    /// </summary>
    /// <param name="handHistoryJson">pełny dokument z kolumny hand_history_json.</param>
    /// <returns>0 gdy brak action lub zły JSON.</returns>
    public static int MaxPotFromReplayJson(string handHistoryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                return 0;
            var max = 0;
            foreach (var ev in events.EnumerateArray())
            {
                if (!ev.TryGetProperty("ev", out var evType) || evType.GetString() != "action")
                    continue;
                if (ev.TryGetProperty("pot", out var potEl) && potEl.TryGetInt32(out var pot))
                {
                    if (pot > max)
                        max = pot;
                }
            }

            return max;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>liczy akcje LLM z niepustym prompt_before_action — metryka serii turniejów.</summary>
    /// <param name="handHistoryJson">historia jednej ręki.</param>
    /// <returns>liczba promptów w tej ręce.</returns>
    public static int CountPromptsFromReplayJson(string handHistoryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                return 0;
            var n = 0;
            foreach (var ev in events.EnumerateArray())
            {
                if (!ev.TryGetProperty("ev", out var t) || t.GetString() != "action")
                    continue;
                if (!ev.TryGetProperty("prompt_before_action", out var p) || p.ValueKind != JsonValueKind.String)
                    continue;
                if (!string.IsNullOrWhiteSpace(p.GetString()))
                    n++;
            }

            return n;
        }
        catch
        {
            return 0;
        }
    }

    public static IReadOnlyList<string> RosterNamesFromReplayJson(string handHistoryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            foreach (var ev in events.EnumerateArray())
            {
                if (!ev.TryGetProperty("ev", out var t) || t.GetString() != "replay_header")
                    continue;
                if (!ev.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
                    return Array.Empty<string>();
                var list = new List<string>();
                foreach (var p in players.EnumerateArray())
                {
                    var name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(name))
                        list.Add(name);
                }

                return list;
            }
        }
        catch
        {
        }

        return Array.Empty<string>();
    }

    public static Dictionary<string, int> StartStacksFromReplayJson(string handHistoryJson)
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                return dict;
            foreach (var ev in events.EnumerateArray())
            {
                if (!ev.TryGetProperty("ev", out var t) || t.GetString() != "start_hand")
                    continue;
                var name = ev.TryGetProperty("player", out var pn) ? pn.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(name))
                    continue;
                var stack = ev.TryGetProperty("stack", out var s) && s.TryGetInt32(out var v) ? v : 0;
                dict[name] = stack;
            }
        }
        catch
        {
        }

        return dict;
    }

    public static Dictionary<string, int> FinalStacksFromReplayJson(string handHistoryJson)
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            if (!doc.RootElement.TryGetProperty("final_stacks", out var fs))
                return dict;
            if (fs.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in fs.EnumerateArray())
                {
                    var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (el.TryGetProperty("stack", out var st) && st.TryGetInt32(out var chips) && !string.IsNullOrEmpty(name))
                        dict[name] = chips;
                }

                return dict;
            }

            if (fs.ValueKind != JsonValueKind.Object)
                return dict;
            foreach (var prop in fs.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var chips))
                    dict[prop.Name] = chips;
            }
        }
        catch
        {
        }

        return dict;
    }

    public static IReadOnlyList<int> FinalStacksInOrderFromReplayJson(string handHistoryJson)
    {
        var list = new List<int>();
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            if (!doc.RootElement.TryGetProperty("final_stacks", out var fs) || fs.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var el in fs.EnumerateArray())
            {
                if (el.TryGetProperty("stack", out var st) && st.TryGetInt32(out var chips))
                    list.Add(chips);
                else if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v))
                    list.Add(v);
            }
        }
        catch
        {
        }

        return list;
    }

    public static IReadOnlyList<string> WinnersFromReplayJson(string handHistoryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            if (!doc.RootElement.TryGetProperty("winners", out var w) || w.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in w.EnumerateArray())
            {
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s))
                    list.Add(s);
            }

            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool TournamentFinishedInReplayJson(string handHistoryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            return doc.RootElement.TryGetProperty("tournament_finished", out var tf) && tf.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    public static string? TournamentWinnerFromReplayJson(string handHistoryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            return doc.RootElement.TryGetProperty("tournament_winner", out var tw) ? tw.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public static string FormatHandHistoryForExport(string handHistoryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handHistoryJson);
            var root = doc.RootElement;
            var lines = new List<string>();
            if (root.TryGetProperty("hand_number", out var hn) && hn.TryGetInt32(out var handNum))
                lines.Add($"Hand #{handNum}");

            if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                foreach (var ev in events.EnumerateArray())
                {
                    if (!ev.TryGetProperty("ev", out var typeEl))
                        continue;
                    var type = typeEl.GetString() ?? "";
                    switch (type)
                    {
                        case "replay_header":
                            break;
                        case "start_hand":
                            AppendStartHand(lines, ev);
                            break;
                        case "start_round":
                            AppendStartRound(lines, ev);
                            break;
                        case "action":
                            AppendAction(lines, ev);
                            break;
                        case "showdown":
                            AppendShowdown(lines, ev);
                            break;
                        case "hand_end":
                            AppendHandEnd(lines, ev);
                            break;
                    }
                }
            }

            if (lines.Count == 0 || !lines.Any(l => l.StartsWith("Winners:", StringComparison.Ordinal)))
            {
                if (root.TryGetProperty("winners", out var winners) && winners.ValueKind == JsonValueKind.Array)
                {
                    var names = winners.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x));
                    lines.Add("Winners: " + string.Join(", ", names));
                }
            }

            if (lines.Count == 0)
                return handHistoryJson;
            return string.Join('\n', lines);
        }
        catch
        {
            return handHistoryJson;
        }
    }

    private static void AppendStartHand(List<string> lines, JsonElement ev)
    {
        var player = ev.TryGetProperty("player", out var pn) ? pn.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(player))
            return;
        var stack = ev.TryGetProperty("stack", out var s) && s.TryGetInt32(out var st) ? st : (int?)null;
        var cards = FormatCardsProperty(ev, "cards");
        var tail = stack.HasValue ? $" stack {stack}" : "";
        var cardPart = string.IsNullOrEmpty(cards) ? "" : $" [{cards}]";
        lines.Add($"{player} dealt{cardPart}{tail}");
    }

    private static void AppendStartRound(List<string> lines, JsonElement ev)
    {
        var round = ev.TryGetProperty("round", out var r) ? NormalizeRoundLabel(r.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(round))
            return;
        var board = FormatCardsProperty(ev, "community_cards");
        lines.Add(string.IsNullOrEmpty(board) ? $"--- {round} ---" : $"--- {round} --- board: {board}");
    }

    private static void AppendAction(List<string> lines, JsonElement ev)
    {
        var player = ev.TryGetProperty("player", out var pn) ? pn.GetString() ?? "" : "";
        var round = ev.TryGetProperty("round", out var rd) ? NormalizeRoundLabel(rd.GetString() ?? "") : "";
        var action = ev.TryGetProperty("action", out var ac) ? ac.GetString() ?? "" : "";
        var pot = ev.TryGetProperty("pot", out var pe) && pe.TryGetInt32(out var p) ? p : (int?)null;
        var toCall = ev.TryGetProperty("moneyToCall", out var mc) && mc.TryGetInt32(out var c) ? c : (int?)null;
        var stack = ev.TryGetProperty("stack", out var se) && se.TryGetInt32(out var s) ? s : (int?)null;
        var meta = new StringBuilder();
        if (pot.HasValue)
            meta.Append($" pot={pot}");
        if (toCall.HasValue)
            meta.Append($" to_call={toCall}");
        if (stack.HasValue)
            meta.Append($" stack={stack}");
        lines.Add($"{player} [{round}]: {action}{meta}");
        var prompt = ev.TryGetProperty("prompt_before_action", out var pr) ? pr.GetString() : null;
        if (!string.IsNullOrWhiteSpace(prompt))
            AppendBlock(lines, "prompt", prompt);
        var thought = ev.TryGetProperty("thought_before_action", out var th) ? th.GetString() : null;
        if (!string.IsNullOrWhiteSpace(thought))
            AppendBlock(lines, "thought", thought);
    }

    private static void AppendBlock(List<string> lines, string label, string text)
    {
        lines.Add($"  [{label}]");
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            lines.Add("    " + line);
    }

    private static void AppendShowdown(List<string> lines, JsonElement ev)
    {
        lines.Add("--- Showdown ---");
        if (!ev.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Object)
            return;
        foreach (var prop in cards.EnumerateObject())
        {
            var hand = FormatCardsElement(prop.Value);
            lines.Add($"{prop.Name}: {hand}");
        }
    }

    private static void AppendHandEnd(List<string> lines, JsonElement ev)
    {
        if (ev.TryGetProperty("winners", out var w) && w.ValueKind == JsonValueKind.Array)
        {
            var names = w.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x));
            lines.Add("Winners: " + string.Join(", ", names));
        }

        if (ev.TryGetProperty("stacks", out var stacks) && stacks.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var el in stacks.EnumerateArray())
            {
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var stack = el.TryGetProperty("stack", out var s) && s.TryGetInt32(out var v) ? v : 0;
                if (!string.IsNullOrEmpty(name))
                    parts.Add($"{name}={stack}");
            }

            if (parts.Count > 0)
                lines.Add("Stacks: " + string.Join(", ", parts));
        }

        if (ev.TryGetProperty("tournamentFinished", out var tf) && tf.GetBoolean()
            && ev.TryGetProperty("tournamentWinner", out var tw))
        {
            var winner = tw.GetString();
            if (!string.IsNullOrEmpty(winner))
                lines.Add($"Tournament finished. Winner: {winner}");
        }
    }

    private static string FormatCardsProperty(JsonElement ev, string propertyName)
    {
        return ev.TryGetProperty(propertyName, out var cards) ? FormatCardsElement(cards) : "";
    }

    private static string FormatCardsElement(JsonElement cards)
    {
        if (cards.ValueKind != JsonValueKind.Array)
            return "";
        var parts = new List<string>();
        foreach (var c in cards.EnumerateArray())
        {
            var rank = c.TryGetProperty("rank", out var r) ? r.GetString() ?? "" : "";
            var suit = c.TryGetProperty("suit", out var su) ? su.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(rank))
                parts.Add($"{rank}{suit}");
        }

        return string.Join(' ', parts);
    }

    private static string NormalizeRoundLabel(string round) =>
        round switch
        {
            "PreFlop" => "Pre-Flop",
            _ => round
        };
}
