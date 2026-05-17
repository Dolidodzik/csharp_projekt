using System.Text.Json;

namespace PokerApp;

public static class ReplayJsonUtil
{
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
}
