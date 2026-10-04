using System.Globalization;
using System.Text;

namespace ChessCore.Uci;

public enum EngineLogDirection
{
    ToEngine,
    FromEngine,
    StdErr,
}

public readonly record struct EngineLogEntry(EngineLogDirection Direction, string Text);

/// <summary>
/// The engine's answer to "go": "bestmove e2e4 ponder e7e5". <paramref name="Elapsed"/> is
/// the time from sending "go" to reading "bestmove", measured where the engine's output is
/// read, so a busy UI thread can't add to it; it is what a match charges to the clock.
/// </summary>
public readonly record struct UciBestMove(string Move, string? Ponder, TimeSpan Elapsed = default);

public class UciEngineException : Exception
{
    public UciEngineException(string message)
        : base(message)
    {
    }

    public UciEngineException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>What to put after "go".</summary>
public sealed record SearchLimits
{
    public int? MoveTimeMs { get; init; }

    public int? Depth { get; init; }

    public long? Nodes { get; init; }

    public long? WhiteTimeMs { get; init; }

    public long? BlackTimeMs { get; init; }

    public long? WhiteIncrementMs { get; init; }

    public long? BlackIncrementMs { get; init; }

    /// <summary>Search until told to "stop" (used for analysis).</summary>
    public bool Infinite { get; init; }

    public string ToGoCommand()
    {
        var sb = new StringBuilder("go");
        var inv = CultureInfo.InvariantCulture;
        if (WhiteTimeMs is { } wtime) sb.Append(inv, $" wtime {wtime}");
        if (BlackTimeMs is { } btime) sb.Append(inv, $" btime {btime}");
        if (WhiteIncrementMs is { } winc) sb.Append(inv, $" winc {winc}");
        if (BlackIncrementMs is { } binc) sb.Append(inv, $" binc {binc}");
        if (Depth is { } depth) sb.Append(inv, $" depth {depth}");
        if (Nodes is { } nodes) sb.Append(inv, $" nodes {nodes}");
        if (MoveTimeMs is { } movetime) sb.Append(inv, $" movetime {movetime}");
        if (Infinite) sb.Append(" infinite");
        return sb.ToString();
    }
}

public enum UciOptionType
{
    Check,
    Spin,
    Combo,
    Button,
    String,
}

/// <summary>An option the engine advertises during the handshake.</summary>
public sealed record UciOption(string Name, UciOptionType Type, string? Default, long? Min, long? Max, IReadOnlyList<string> Choices)
{
    private static readonly HashSet<string> Keywords = ["name", "type", "default", "min", "max", "var"];

    /// <summary>Parses e.g. "option name Hash type spin default 16 min 1 max 1024".</summary>
    public static UciOption? Parse(string line)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens[0] != "option")
            return null;

        string name = "", type = "", min = "", max = "";
        string? defaultValue = null;
        var choices = new List<string>();
        string? field = null;

        static string Join(string current, string token) => current.Length == 0 ? token : current + " " + token;

        foreach (var token in tokens.Skip(1))
        {
            if (Keywords.Contains(token))
            {
                field = token;
                if (token == "var") choices.Add("");
                if (token == "default") defaultValue = "";
                continue;
            }
            switch (field)
            {
                case "name": name = Join(name, token); break;
                case "type": type = Join(type, token); break;
                case "default": defaultValue = Join(defaultValue!, token); break;
                case "min": min = token; break;
                case "max": max = token; break;
                case "var": choices[^1] = Join(choices[^1], token); break;
            }
        }

        UciOptionType? parsedType = type switch
        {
            "check" => UciOptionType.Check,
            "spin" => UciOptionType.Spin,
            "combo" => UciOptionType.Combo,
            "button" => UciOptionType.Button,
            "string" => UciOptionType.String,
            _ => null,
        };
        if (name.Length == 0 || parsedType is null)
            return null;
        if (defaultValue == "<empty>")
            defaultValue = "";

        return new UciOption(name, parsedType.Value, defaultValue, ParseLong(min), ParseLong(max), choices);
    }

    private static long? ParseLong(string text) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
}

/// <summary>One "info" line sent while the engine searches.</summary>
public sealed record UciInfo
{
    public int? Depth { get; init; }

    public int? SelectiveDepth { get; init; }

    public int? MultiPv { get; init; }

    /// <summary>Centipawns from the point of view of the side to move.</summary>
    public int? ScoreCentipawns { get; init; }

    /// <summary>Mate in N moves from the side to move's point of view; negative if being mated.</summary>
    public int? ScoreMate { get; init; }

    public bool IsLowerBound { get; init; }

    public bool IsUpperBound { get; init; }

    public long? Nodes { get; init; }

    public long? NodesPerSecond { get; init; }

    public long? TimeMs { get; init; }

    public int? HashFull { get; init; }

    public IReadOnlyList<string> Pv { get; init; } = [];

    public string? Text { get; init; }

    public bool HasScore => ScoreCentipawns.HasValue || ScoreMate.HasValue;

    /// <summary>Score from White's point of view: "+0.34", "-1.20", "#3", "#-2".</summary>
    public string? FormatScore(PieceColor sideToMove)
    {
        int sign = sideToMove == PieceColor.White ? 1 : -1;
        if (ScoreMate is { } mate)
            return "#" + (mate * sign).ToString(CultureInfo.InvariantCulture);
        if (ScoreCentipawns is { } cp)
            return (cp * sign / 100.0).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
        return null;
    }

    public static UciInfo? Parse(string line)
    {
        var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0 || t[0] != "info")
            return null;

        int? depth = null, selDepth = null, multiPv = null, cp = null, mate = null, hashFull = null;
        long? nodes = null, nps = null, time = null;
        bool lower = false, upper = false;
        IReadOnlyList<string> pv = [];
        string? text = null;

        for (int i = 1; i < t.Length; i++)
        {
            switch (t[i])
            {
                case "depth": depth = Int(t, ++i); break;
                case "seldepth": selDepth = Int(t, ++i); break;
                case "multipv": multiPv = Int(t, ++i); break;
                case "nodes": nodes = Long(t, ++i); break;
                case "nps": nps = Long(t, ++i); break;
                case "time": time = Long(t, ++i); break;
                case "hashfull": hashFull = Int(t, ++i); break;
                case "currmove" or "currmovenumber" or "tbhits" or "sbhits" or "cpuload": i++; break;
                case "wdl": i += 3; break;
                case "score":
                    while (i + 1 < t.Length)
                    {
                        switch (t[i + 1])
                        {
                            case "cp": cp = Int(t, i + 2); i += 2; continue;
                            case "mate": mate = Int(t, i + 2); i += 2; continue;
                            case "lowerbound": lower = true; i++; continue;
                            case "upperbound": upper = true; i++; continue;
                        }
                        break;
                    }
                    break;
                case "pv":
                    pv = t[(i + 1)..];
                    i = t.Length;
                    break;
                case "string":
                    text = string.Join(' ', t[(i + 1)..]);
                    i = t.Length;
                    break;
                case "refutation" or "currline":
                    i = t.Length;
                    break;
            }
        }

        return new UciInfo
        {
            Depth = depth,
            SelectiveDepth = selDepth,
            MultiPv = multiPv,
            ScoreCentipawns = cp,
            ScoreMate = mate,
            IsLowerBound = lower,
            IsUpperBound = upper,
            Nodes = nodes,
            NodesPerSecond = nps,
            TimeMs = time,
            HashFull = hashFull,
            Pv = pv,
            Text = text,
        };
    }

    private static int? Int(string[] tokens, int index) =>
        index < tokens.Length && int.TryParse(tokens[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static long? Long(string[] tokens, int index) =>
        index < tokens.Length && long.TryParse(tokens[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;
}
