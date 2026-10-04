using ChessCore;
using ChessCore.Uci;

namespace ChessGui;

/// <summary>One of the engines the GUI runs side by side, with its latest search output.</summary>
public sealed class EngineSlot(int index)
{
    public int Index { get; } = index;

    public string Label => $"Engine {Index + 1}";

    public UciEngine? Engine { get; internal set; }

    public bool IsStarting { get; internal set; }

    public string Name => Engine?.Name ?? $"{Label} (not loaded)";

    /// <summary>Searching for a move to play.</summary>
    public bool IsThinking => SearchCts is not null;

    /// <summary>Analysing the current position in the background.</summary>
    public bool IsAnalysing => AnalysisCts is not null;

    /// <summary>Latest search output, from playing a move or from analysis.</summary>
    public UciInfo? Info { get; internal set; }

    /// <summary>The position <see cref="Info"/> was searched from.</summary>
    public Position? InfoPosition { get; internal set; }

    internal CancellationTokenSource? SearchCts { get; set; }

    internal CancellationTokenSource? AnalysisCts { get; set; }

    /// <summary>Completes when the engine has acknowledged the latest "ucinewgame".</summary>
    internal Task Ready { get; set; } = Task.CompletedTask;
}
