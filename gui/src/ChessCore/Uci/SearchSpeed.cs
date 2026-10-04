using System.Globalization;

namespace ChessCore.Uci;

/// <summary>
/// How fast and how much one engine searched over a series of moves: the nodes it
/// reported and the time it reported them in, summed over every search. The average is total nodes over
/// total time, so a long search counts for more than a short one -- the same figure an
/// engine's own "nps" gives for a single search.
/// </summary>
public sealed class SearchSpeed
{
    public int Searches { get; private set; }

    public long Nodes { get; private set; }

    public long Milliseconds { get; private set; }

    /// <summary>
    /// Adds one finished search from what the engine last reported. Searches that reported
    /// no node count or no time (or a time of 0 ms) are skipped: they say nothing about speed.
    /// </summary>
    public void Add(long? nodes, long? timeMs, long? nodesPerSecond = null)
    {
        if (timeMs is not { } ms || ms <= 0)
            return;
        // An engine that sends "nps" and "time" but no "nodes" still tells us enough.
        long? n = nodes ?? (nodesPerSecond is { } nps ? nps * ms / 1000 : null);
        if (n is not { } count || count < 0)
            return;
        Searches++;
        Nodes += count;
        Milliseconds += ms;
    }

    public void Add(SearchInfoRecorder recorder) => Add(recorder.Nodes, recorder.TimeMs, recorder.NodesPerSecond);

    /// <summary>Average nodes per second, or null before any search has been counted.</summary>
    public double? NodesPerSecond => Milliseconds > 0 ? Nodes * 1000.0 / Milliseconds : null;

    /// <summary>Average nodes searched per move, or null before any search has been counted.</summary>
    public double? NodesPerMove => Searches > 0 ? (double)Nodes / Searches : null;

    /// <summary>Average thinking time per move in milliseconds, or null before any search has been counted.</summary>
    public double? MillisecondsPerMove => Searches > 0 ? (double)Milliseconds / Searches : null;

    /// <summary>Time per move as "312 ms" or "1.24 s", or "—" when nothing has been counted.</summary>
    public string DescribeTimePerMove()
    {
        if (MillisecondsPerMove is not { } ms)
            return "—";
        return ms >= 1000
            ? (ms / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " s"
            : ms.ToString("0", CultureInfo.InvariantCulture) + " ms";
    }

    /// <summary>Nodes per second as "2.31M", "845.2k", "950", or "—" when nothing has been counted.</summary>
    public string Describe() => NodesPerSecond is { } nps ? Format(nps) : "—";

    /// <summary>Nodes per move in the same style, or "—" when nothing has been counted.</summary>
    public string DescribeNodesPerMove() => NodesPerMove is { } npm ? Format(npm) : "—";

    public static string Format(double nodesPerSecond)
    {
        var inv = CultureInfo.InvariantCulture;
        return nodesPerSecond switch
        {
            >= 1_000_000 => (nodesPerSecond / 1_000_000).ToString("0.00", inv) + "M",
            >= 1_000 => (nodesPerSecond / 1_000).ToString("0.0", inv) + "k",
            _ => nodesPerSecond.ToString("0", inv),
        };
    }
}

/// <summary>
/// Keeps the latest node count, time and speed an engine reported during one search, so
/// they can be read once its "bestmove" has arrived. Lines without them ("info string",
/// "info currmove") leave the last values alone.
/// </summary>
/// <remarks>
/// The engine's output is read on a background thread, which is where Report runs, and
/// every line before "bestmove" has been reported by the time the search completes. Any
/// other progress handler (the analysis panel) is passed on unchanged.
/// </remarks>
public sealed class SearchInfoRecorder(IProgress<UciInfo>? forward = null) : IProgress<UciInfo>
{
    private readonly Lock gate = new();
    private long? nodes;
    private long? timeMs;
    private long? nodesPerSecond;

    public long? Nodes { get { lock (gate) return nodes; } }

    public long? TimeMs { get { lock (gate) return timeMs; } }

    public long? NodesPerSecond { get { lock (gate) return nodesPerSecond; } }

    public void Report(UciInfo value)
    {
        lock (gate)
        {
            if (value.Nodes is { } n) nodes = n;
            if (value.TimeMs is { } t) timeMs = t;
            if (value.NodesPerSecond is { } nps) nodesPerSecond = nps;
        }
        forward?.Report(value);
    }
}
