using System.Globalization;

namespace ChessCore;

/// <summary>
/// How a series of games ended: one count per <see cref="GameEndReason"/>, and how long
/// the games lasted. The match score says who won; this says how — for example whether a
/// change that was meant to avoid repetitions just moved the draws to the fifty-move rule.
/// </summary>
public sealed class GameEndTally
{
    private readonly Dictionary<GameEndReason, int> counts = [];
    private long decisivePlies;
    private long drawnPlies;

    public int Games => Decisive + Draws;

    public int Decisive { get; private set; }

    public int Draws { get; private set; }

    /// <summary>Records a finished game and its length in plies (half-moves).</summary>
    public void Add(GameResult result, int plies)
    {
        if (!result.IsOver)
            return;
        counts[result.Reason] = Count(result.Reason) + 1;
        if (result.Outcome == GameOutcome.Draw)
        {
            Draws++;
            drawnPlies += plies;
        }
        else
        {
            Decisive++;
            decisivePlies += plies;
        }
    }

    public int Count(GameEndReason reason) => counts.GetValueOrDefault(reason);

    /// <summary>Average length in full moves, or null with no games of that kind.</summary>
    public double? AverageMoves(bool? drawn = null)
    {
        long plies = drawn switch { true => drawnPlies, false => decisivePlies, null => drawnPlies + decisivePlies };
        int games = drawn switch { true => Draws, false => Decisive, null => Games };
        return games == 0 ? null : plies / 2.0 / games;
    }

    /// <summary>
    /// The rows of the Match tab's "How games ended" table: a total for decisive games,
    /// the ways they were decided, a total for draws, and the ways they were drawn.
    /// Checkmate, repetition and the fifty-move rule are always listed; the rarer
    /// endings only when they occurred.
    /// </summary>
    public IReadOnlyList<(string Label, int Count, bool IsTotal)> Rows()
    {
        var rows = new List<(string, int, bool)> { ("Decisive", Decisive, true), ("Checkmate", Count(GameEndReason.Checkmate), false) };
        AddIfAny(GameEndReason.Timeout, "Lost on time");
        AddIfAny(GameEndReason.IllegalMove, "Illegal move");
        rows.Add(("Drawn", Draws, true));
        rows.Add(("Repetition", Count(GameEndReason.ThreefoldRepetition), false));
        rows.Add(("Fifty-move rule", Count(GameEndReason.FiftyMoveRule), false));
        AddIfAny(GameEndReason.Stalemate, "Stalemate");
        AddIfAny(GameEndReason.InsufficientMaterial, "Insufficient material");
        AddIfAny(GameEndReason.TimeoutVsInsufficientMaterial, "Time out, no mating material");
        return rows;

        void AddIfAny(GameEndReason reason, string label)
        {
            if (Count(reason) > 0)
                rows.Add((label, Count(reason), false));
        }
    }

    /// <summary>
    /// Three lines of plain text: decisive results, draws, and game length. Repetition
    /// and the fifty-move rule are always listed; the rarer reasons only when they occurred.
    /// </summary>
    public string Describe()
    {
        if (Games == 0)
            return "";

        var inv = CultureInfo.InvariantCulture;
        var wins = new List<string> { $"{Count(GameEndReason.Checkmate)} checkmate" };
        AddIfAny(wins, GameEndReason.Timeout, "on time");
        AddIfAny(wins, GameEndReason.IllegalMove, "illegal move");

        var draws = new List<string>
        {
            $"{Count(GameEndReason.ThreefoldRepetition)} repetition",
            $"{Count(GameEndReason.FiftyMoveRule)} fifty-move rule",
        };
        AddIfAny(draws, GameEndReason.Stalemate, "stalemate");
        AddIfAny(draws, GameEndReason.InsufficientMaterial, "insufficient material");
        AddIfAny(draws, GameEndReason.TimeoutVsInsufficientMaterial, "time, no mating material");

        string Moves(double? value) => value is { } v ? v.ToString("0", inv) : "—";
        return $"Decisive {Decisive}: {string.Join(" · ", wins)}\n" +
               $"Drawn {Draws}: {string.Join(" · ", draws)}\n" +
               $"Average length {Moves(AverageMoves())} moves (decisive {Moves(AverageMoves(false))}, drawn {Moves(AverageMoves(true))})";

        void AddIfAny(List<string> list, GameEndReason reason, string label)
        {
            if (Count(reason) > 0)
                list.Add($"{Count(reason)} {label}");
        }
    }
}
