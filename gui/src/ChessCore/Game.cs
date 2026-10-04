using System.Globalization;
using System.Text;

namespace ChessCore;

public enum GameOutcome
{
    Ongoing,
    WhiteWins,
    BlackWins,
    Draw,
}

public enum GameEndReason
{
    None,
    Checkmate,
    Stalemate,
    InsufficientMaterial,
    FiftyMoveRule,
    ThreefoldRepetition,
    Timeout,
    TimeoutVsInsufficientMaterial,
    IllegalMove,

    /// <summary>Ended early by agreement of both engines' scores (a decided or a dead level position).</summary>
    Adjudication,
}

public readonly record struct GameResult(GameOutcome Outcome, GameEndReason Reason)
{
    public static GameResult Ongoing => default;

    public bool IsOver => Outcome != GameOutcome.Ongoing;

    public string PgnResult => Outcome switch
    {
        GameOutcome.WhiteWins => "1-0",
        GameOutcome.BlackWins => "0-1",
        GameOutcome.Draw => "1/2-1/2",
        _ => "*",
    };

    public static GameResult Win(PieceColor winner, GameEndReason reason) =>
        new(winner == PieceColor.White ? GameOutcome.WhiteWins : GameOutcome.BlackWins, reason);

    public static GameResult Drawn(GameEndReason reason) => new(GameOutcome.Draw, reason);

    public string Describe()
    {
        string winner = Outcome == GameOutcome.WhiteWins ? "White" : "Black";
        return Reason switch
        {
            GameEndReason.Checkmate => $"Checkmate — {winner} wins",
            GameEndReason.Timeout => $"{winner} wins on time",
            GameEndReason.Stalemate => "Draw by stalemate",
            GameEndReason.InsufficientMaterial => "Draw by insufficient material",
            GameEndReason.FiftyMoveRule => "Draw by the fifty-move rule",
            GameEndReason.ThreefoldRepetition => "Draw by threefold repetition",
            GameEndReason.TimeoutVsInsufficientMaterial => "Draw — time ran out, but the opponent cannot mate",
            GameEndReason.IllegalMove => $"{winner} wins — the opponent played an illegal move",
            GameEndReason.Adjudication => Outcome == GameOutcome.Draw ? "Draw by adjudication" : $"{winner} wins by adjudication",
            _ => "",
        };
    }
}

/// <summary>A game: start position, the moves played, and rule-based result detection.</summary>
public sealed class Game
{
    private readonly List<Position> positions = [];
    private readonly List<string> repetitionKeys = [];
    private readonly List<Move> moves = [];
    private readonly List<string> sanMoves = [];
    private GameResult? adjudicated;

    public Game()
        : this(Position.Start)
    {
    }

    public Game(Position start)
    {
        positions.Add(start);
        repetitionKeys.Add(start.RepetitionKey());
    }

    public Position StartPosition => positions[0];

    public Position Position => positions[^1];

    public IReadOnlyList<Move> Moves => moves;

    public IReadOnlyList<string> SanMoves => sanMoves;

    public int PlyCount => moves.Count;

    public Move? LastMove => moves.Count > 0 ? moves[^1] : null;

    public GameResult Result
    {
        get
        {
            if (adjudicated is { } result)
                return result;

            var p = Position;
            if (p.LegalMoves.Count == 0)
                return p.IsInCheck ? GameResult.Win(p.SideToMove.Opposite(), GameEndReason.Checkmate) : GameResult.Drawn(GameEndReason.Stalemate);
            if (p.IsInsufficientMaterial())
                return GameResult.Drawn(GameEndReason.InsufficientMaterial);
            if (p.HalfmoveClock >= 100)
                return GameResult.Drawn(GameEndReason.FiftyMoveRule);

            var key = repetitionKeys[^1];
            if (repetitionKeys.Count(k => k == key) >= 3)
                return GameResult.Drawn(GameEndReason.ThreefoldRepetition);

            return GameResult.Ongoing;
        }
    }

    /// <summary>Position before the given ply (0 = start position).</summary>
    public Position PositionBefore(int ply) => positions[ply];

    /// <summary>Plays a legal move and returns its SAN.</summary>
    public string Play(Move move)
    {
        if (Result.IsOver)
            throw new InvalidOperationException("The game is already over.");

        var before = Position;
        var san = San.Format(before, move);
        var after = before.Play(move);
        positions.Add(after);
        repetitionKeys.Add(after.RepetitionKey());
        moves.Add(move);
        sanMoves.Add(san);
        return san;
    }

    public bool Undo()
    {
        if (moves.Count == 0)
            return false;
        adjudicated = null;
        positions.RemoveAt(positions.Count - 1);
        repetitionKeys.RemoveAt(repetitionKeys.Count - 1);
        moves.RemoveAt(moves.Count - 1);
        sanMoves.RemoveAt(sanMoves.Count - 1);
        return true;
    }

    /// <summary>Ends the game for a reason the board can't see (e.g. a flag falling).</summary>
    public void Adjudicate(GameResult result) => adjudicated = result;

    /// <summary>The UCI command that sets up this game: "position startpos moves e2e4 e7e5".</summary>
    public string ToUciPositionCommand()
    {
        var sb = new StringBuilder("position ");
        var fen = StartPosition.ToFen();
        sb.Append(fen == Position.StartFen ? "startpos" : "fen " + fen);
        if (moves.Count > 0)
        {
            sb.Append(" moves");
            foreach (var move in moves)
                sb.Append(' ').Append(move.ToUci());
        }
        return sb.ToString();
    }

    public string ToPgn(string white, string black, DateTime? date = null, string eventName = "Casual game", string round = "-",
                        string? timeControl = null)
    {
        var result = Result;
        var sb = new StringBuilder();
        void Tag(string name, string value) =>
            sb.Append('[').Append(name).Append(" \"")
              .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""))
              .Append("\"]\n");

        Tag("Event", eventName);
        Tag("Site", "Chess GUI");
        Tag("Date", (date ?? DateTime.Now).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture));
        Tag("Round", round);
        Tag("White", white);
        Tag("Black", black);
        Tag("Result", result.PgnResult);
        if (timeControl is not null)
            Tag("TimeControl", timeControl);
        var fen = StartPosition.ToFen();
        if (fen != Position.StartFen)
        {
            Tag("SetUp", "1");
            Tag("FEN", fen);
        }
        if (result.Reason is GameEndReason.Timeout or GameEndReason.TimeoutVsInsufficientMaterial)
            Tag("Termination", "time forfeit");
        else if (result.Reason == GameEndReason.IllegalMove)
            Tag("Termination", "rules infraction");
        else if (result.Reason == GameEndReason.Adjudication)
            Tag("Termination", "adjudication");
        sb.Append('\n');

        var tokens = new List<string>();
        for (int i = 0; i < moves.Count; i++)
        {
            var before = positions[i];
            if (before.SideToMove == PieceColor.White)
                tokens.Add(before.FullmoveNumber.ToString(CultureInfo.InvariantCulture) + ".");
            else if (i == 0)
                tokens.Add(before.FullmoveNumber.ToString(CultureInfo.InvariantCulture) + "...");
            tokens.Add(sanMoves[i]);
        }
        tokens.Add(result.PgnResult);

        // PGN export format keeps movetext lines under 80 characters.
        int lineLength = 0;
        foreach (var token in tokens)
        {
            if (lineLength > 0 && lineLength + 1 + token.Length > 79)
            {
                sb.Append('\n');
                lineLength = 0;
            }
            else if (lineLength > 0)
            {
                sb.Append(' ');
                lineLength++;
            }
            sb.Append(token);
            lineLength += token.Length;
        }
        sb.Append('\n');
        return sb.ToString();
    }
}
