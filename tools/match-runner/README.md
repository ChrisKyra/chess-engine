# match-runner

Plays engine matches from the command line, several games at once, using the GUI's
own `ChessCore` code: the same 293 openings (each played from both sides), clocks,
SPRT and adjudication as the Match tab, without the window. It is what the engine
versions are tested with, and it also generates the positions the evaluation is
tuned on.

    cd tools/match-runner
    dotnet run -c Release -- --engine1 ../../Engine_12/engine --engine2 ../../Engine_11/engine \
        --tc 8+0.08 --concurrency 8 --sprt 0,10 --games 300 --option Threads=1 --option Hash=64

(If `dotnet` was installed into `~/.dotnet`, put it on the `PATH` and set
`DOTNET_ROOT=$HOME/.dotnet` to run the built `bin/Release/net10.0/match-runner`
directly.)

## Testing

Each game alternates colours; an opening's two games form a pair, and the SPRT
(H0 Elo0, H1 Elo1, 5% error each way) is computed on pairs and stops the run once
it decides. `--no-sprt` plays every game for an Elo estimate. A progress line is
printed every 20 games: score, Elo with its 95% margin, the LLR and the pair counts.

**Adjudication** (on unless `--no-adjudication`): a game is scored as won once the
last six scores, from both engines, all give the same side at least 10 pawns, and
drawn from move 40 on once the last ten scores are all within 0.10 of level. This
saves time without changing results in any meaningful way.

## Data for tuning

    dotnet run -c Release -- --engine1 ../../Engine_11/engine --engine2 ../../Engine_11/engine \
        --nodes 10000 --random-plies 4 --games 20000 --concurrency 8 --option Threads=1 \
        --datagen positions.txt

writes one line per quiet position, `FEN;result` with the result from White's point
of view (`1.0`, `0.5`, `0.0`). A position is recorded when it is at least
`--skip-plies` (default 8) plies past the opening, the side to move is not in check,
and the engine's move is not a capture or promotion. `Engine_11`'s tuner
(`make tuner && ./tuner tune positions.txt`) then fits the evaluation to those
results; see `Engine_11/README.md`.

## Options

Run without arguments for the list: engines, games, concurrency, `--tc`,
`--movetime` or `--nodes`, `--sprt`/`--no-sprt`, `--no-adjudication`,
`--random-plies`, `--option NAME=VALUE`, `--pgn`, `--datagen`, `--skip-plies`,
`--report`.
