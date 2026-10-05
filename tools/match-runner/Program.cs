using System.Globalization;
using ChessCore;
using ChessCore.Uci;

// Plays engine matches from the command line, several games at once, with the same
// openings, clocks, SPRT and adjudication as the GUI's Match tab. Two uses:
//
//   testing:   match-runner --engine1 new/engine --engine2 old/engine --tc 8+0.08 --sprt 0,10
//   tuning:    match-runner --engine1 e/engine --engine2 e/engine --nodes 10000 --datagen positions.txt
//
// Run without arguments for the full list of options (see README.md).

var inv = CultureInfo.InvariantCulture;
Options o;
try
{
    o = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(Options.Usage);
    return 1;
}

var sprt = new Sprt(new SprtSettings { Elo0 = o.Elo0, Elo1 = o.Elo1 });
var score = new MatchScore();
var endings = new GameEndTally();
var unpaired = new Dictionary<int, double>();
var gate = new object();
int nextGame = 0, finished = 0;
long positionsWritten = 0;
bool stop = false;
var started = DateTime.Now;
string name1 = "?", name2 = "?";

async Task<UciEngine> StartEngine(string path, List<(string, string)> ownOptions)
{
    var engine = new UciEngine(path);
    await engine.StartAsync();
    // Options for both engines first, then this engine's own, which win.
    foreach (var (name, value) in o.EngineOptions.Concat(ownOptions))
        await engine.SetOptionAsync(name, value);
    return engine;
}

async Task Worker()
{
    await using var a = await StartEngine(o.Engine1, o.Engine1Options);
    await using var b = await StartEngine(o.Engine2, o.Engine2Options);
    lock (gate) { name1 = a.Name; name2 = b.Name; }
    var adjudicator = new Adjudicator();

    while (true)
    {
        int index;
        lock (gate)
        {
            if (stop || nextGame >= o.Games) return;
            index = nextGame++;
        }

        // Each opening is played twice, Engine 1 with White first.
        var (_, opening) = Openings.ForMatchGame(index / 2, o.RandomPlies);
        bool engine1White = index % 2 == 0;
        var game = new Game(Position.Start);
        foreach (var move in opening) game.Play(move);
        var clock = new GameClock(o.TimeControl);
        adjudicator.Reset();
        var quietPositions = new List<string>();
        await a.NewGameAsync();
        await b.NewGameAsync();

        while (!game.Result.IsOver)
        {
            var position = game.Position;
            var engine = (position.SideToMove == PieceColor.White) == engine1White ? a : b;
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
            if (o.DataFile is not null && game.PlyCount >= opening.Count + o.DataSkipPlies && !position.IsInCheck
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

        var result = game.Result;
        double points1 = result.Outcome switch
        {
            GameOutcome.WhiteWins => engine1White ? 1 : 0,
            GameOutcome.BlackWins => engine1White ? 0 : 1,
            _ => 0.5,
        };
        string label = result.Outcome switch { GameOutcome.WhiteWins => "1.0", GameOutcome.BlackWins => "0.0", _ => "0.5" };

        lock (gate)
        {
            if (o.PgnFile is not null)
            {
                var (white, black) = engine1White ? (name1, name2) : (name2, name1);
                File.AppendAllText(o.PgnFile, game.ToPgn(white, black, eventName: $"{name1} vs {name2}",
                    round: (index + 1).ToString(inv), timeControl: o.TimeControl.PgnTag) + "\n");
            }
            if (o.DataFile is not null && quietPositions.Count > 0)
            {
                File.AppendAllLines(o.DataFile, quietPositions.Select(fen => $"{fen};{label}"));
                positionsWritten += quietPositions.Count;
            }
            if (result.Reason is GameEndReason.Timeout)
                Console.WriteLine($"game {index + 1}: {result.Describe()}");

            score.Add(points1);
            endings.Add(result, game.PlyCount);
            finished++;
            if (unpaired.Remove(index / 2, out var partner)) sprt.AddPair(partner + points1);
            else unpaired[index / 2] = points1;

            bool decided = o.UseSprt && sprt.Decision != SprtDecision.Continue;
            if (finished % o.ReportEvery == 0 || decided) Report();
            if (decided) stop = true;
        }
    }
}

static bool IsEnPassant(Position position, Move move) =>
    position[move.From].Type == PieceType.Pawn && Square.File(move.From) != Square.File(move.To) && position[move.To].IsEmpty;

void Report()
{
    var elo = score.EloEstimate();
    string eloText = elo is { } e ? $"{e.Elo.ToString("+0.0;-0.0", inv)} ± {e.Margin.ToString("0.0", inv)}" : "n/a";
    string line = $"[{(DateTime.Now - started).TotalMinutes.ToString("0.0", inv)} min] games {finished}  " +
                  $"+{score.Wins} ={score.Draws} -{score.Losses}  score {(score.Score * 100).ToString("0.0", inv)}%  Elo {eloText}";
    if (o.UseSprt) line += $"  {sprt.DescribeLlr()}  pairs {string.Join('/', sprt.PairCounts)}";
    if (o.DataFile is not null) line += $"  positions {positionsWritten}";
    Console.WriteLine(line);
}

await Task.WhenAll(Enumerable.Range(0, o.Concurrency).Select(_ => Task.Run(Worker)));
Report();
Console.WriteLine($"{name1} vs {name2} at {o.TimeControl.Describe()}" +
                  (o.UseSprt ? $": SPRT [{o.Elo0.ToString(inv)}, {o.Elo1.ToString(inv)}] {sprt.Decision}" : ""));
Console.WriteLine(endings.Describe());
return 0;

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

sealed class Options
{
    public string Engine1 = "", Engine2 = "";
    public int Games = 400, Concurrency = 4, RandomPlies, ReportEvery = 20, DataSkipPlies = 8;
    public MatchTimeControl TimeControl = new() { Kind = TimeControlKind.Clock, BaseMs = 8000, IncrementMs = 80 };
    public double Elo0, Elo1 = 10;
    public bool UseSprt = true, Adjudicate = true;
    public string? PgnFile, DataFile;
    public List<(string, string)> EngineOptions = [], Engine1Options = [], Engine2Options = [];

    public const string Usage = """
        usage: match-runner --engine1 PATH --engine2 PATH [options]
          --games N            games to play (default 400; with SPRT, the maximum)
          --concurrency N      games at once (default 4)
          --tc BASE+INC        clock in seconds, e.g. 8+0.08 (default)
          --movetime MS        fixed time per move instead of a clock
          --nodes N            fixed nodes per move instead of a clock
          --sprt ELO0,ELO1     SPRT bounds (default 0,10); stops once decided
          --no-sprt            play every game (for an Elo estimate or data)
          --no-adjudication    play every game to its end
          --random-plies N     random legal moves after each opening (default 0)
          --option NAME=VALUE  UCI option for both engines (repeatable), e.g. Threads=1
          --option1 NAME=VALUE UCI option for engine 1 only (repeatable; overrides --option)
          --option2 NAME=VALUE UCI option for engine 2 only, e.g. UCI_Elo=2400
          --pgn FILE           append every game to FILE
          --datagen FILE       append quiet positions with the game result ("FEN;1.0")
          --skip-plies N       with --datagen, plies after the opening not recorded (default 8)
          --report N           progress line every N games (default 20)
        """;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        var inv = CultureInfo.InvariantCulture;
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--engine1": o.Engine1 = Next(); break;
                case "--engine2": o.Engine2 = Next(); break;
                case "--games": o.Games = int.Parse(Next(), inv); break;
                case "--concurrency": o.Concurrency = int.Parse(Next(), inv); break;
                case "--tc":
                    var parts = Next().Split('+');
                    o.TimeControl = new MatchTimeControl
                    {
                        Kind = TimeControlKind.Clock,
                        BaseMs = (long)(double.Parse(parts[0], inv) * 1000),
                        IncrementMs = parts.Length > 1 ? (long)(double.Parse(parts[1], inv) * 1000) : 0,
                    };
                    break;
                case "--movetime": o.TimeControl = new MatchTimeControl { Kind = TimeControlKind.MoveTime, MoveTimeMs = int.Parse(Next(), inv) }; break;
                case "--nodes": o.TimeControl = new MatchTimeControl { Kind = TimeControlKind.Nodes, Nodes = long.Parse(Next(), inv) }; break;
                case "--sprt":
                    var bounds = Next().Split(',');
                    (o.Elo0, o.Elo1, o.UseSprt) = (double.Parse(bounds[0], inv), double.Parse(bounds[1], inv), true);
                    break;
                case "--no-sprt": o.UseSprt = false; break;
                case "--no-adjudication": o.Adjudicate = false; break;
                case "--random-plies": o.RandomPlies = int.Parse(Next(), inv); break;
                case "--option" or "--option1" or "--option2":
                    var target = args[i] == "--option1" ? o.Engine1Options
                               : args[i] == "--option2" ? o.Engine2Options : o.EngineOptions;
                    var kv = Next().Split('=', 2);
                    target.Add((kv[0], kv.Length > 1 ? kv[1] : ""));
                    break;
                case "--pgn": o.PgnFile = Next(); break;
                case "--datagen": o.DataFile = Next(); o.UseSprt = false; break;
                case "--skip-plies": o.DataSkipPlies = int.Parse(Next(), inv); break;
                case "--report": o.ReportEvery = Math.Max(1, int.Parse(Next(), inv)); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }
        if (o.Engine1.Length == 0 || o.Engine2.Length == 0)
            throw new ArgumentException("--engine1 and --engine2 are required");
        return o;
    }
}
