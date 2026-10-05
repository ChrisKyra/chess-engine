using ChessCore;
using ChessCore.Uci;

/// <summary>Plays one game between two running engines, the same way for matches, data and SPSA.</summary>
static class GamePlay
{
    /// <summary>
    /// Plays a game from <paramref name="opening"/> with <paramref name="white"/> and
    /// <paramref name="black"/> on the time control in <paramref name="o"/>, ending it on
    /// the rules, a flag, an illegal move or (if enabled) adjudication. With
    /// <paramref name="quietPositions"/>, the quiet positions of the game are collected for
    /// tuning data.
    /// </summary>
    public static async Task<Game> PlayAsync(UciEngine white, UciEngine black, IReadOnlyList<Move> opening,
                                             Options o, Adjudicator adjudicator, List<string>? quietPositions = null)
    {
        var game = new Game(Position.Start);
        foreach (var move in opening) game.Play(move);
        var clock = new GameClock(o.TimeControl);
        adjudicator.Reset();
        await white.NewGameAsync();
        await black.NewGameAsync();

        while (!game.Result.IsOver)
        {
            var position = game.Position;
            var engine = position.SideToMove == PieceColor.White ? white : black;
            var recorder = new LastScore();
            var best = await engine.SearchAsync(game.ToUciPositionCommand(), clock.Limits(), recorder);

            if (!clock.Charge(position.SideToMove, best.Elapsed))
            {
                game.Adjudicate(GameClock.TimeoutResult(position, position.SideToMove));
                break;
            }
            if (position.FindUciMove(best.Move) is not { } played)
            {
                game.Adjudicate(GameResult.Win(position.SideToMove.Opposite(), GameEndReason.IllegalMove));
                Console.WriteLine($"ILLEGAL MOVE by {engine.Name}: {best.Move} in {position.ToFen()}");
                break;
            }

            // For tuning: quiet positions only -- not in check, and the engine's choice is
            // neither a capture nor a promotion, so the evaluation is not mid-exchange.
            if (quietPositions is not null && game.PlyCount >= opening.Count + o.DataSkipPlies && !position.IsInCheck
                && position[played.To].IsEmpty && played.Promotion == PieceType.None && !IsEnPassant(position, played))
                quietPositions.Add(position.ToFen());

            game.Play(played);
            if (o.Adjudicate)
            {
                adjudicator.Record(position.SideToMove, recorder.Info);
                if (!game.Result.IsOver && adjudicator.Verdict(game.PlyCount) is { } verdict)
                    game.Adjudicate(verdict);
            }
        }
        return game;
    }

    static bool IsEnPassant(Position position, Move move) =>
        position[move.From].Type == PieceType.Pawn && Square.File(move.From) != Square.File(move.To) && position[move.To].IsEmpty;
}

/// <summary>Keeps the last info line that carried a score, for adjudication.</summary>
sealed class LastScore : IProgress<UciInfo>
{
    private readonly Lock gate = new();
    private UciInfo? info;

    public UciInfo? Info { get { lock (gate) return info; } }

    public void Report(UciInfo value)
    {
        if (value.HasScore && !value.IsLowerBound && !value.IsUpperBound)
            lock (gate) info = value;
    }
}
