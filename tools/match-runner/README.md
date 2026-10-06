# match-runner

Plays engine matches from the command line, several games at once, using the GUI's
own `ChessCore` code: the same 293 openings (each played from both sides), clocks,
SPRT and adjudication as the Match tab, without the window. It is what the engine
versions are tested with, and it also generates the positions the evaluation is
tuned on.

    cd tools/match-runner
    dotnet run -c Release -- --engine1 /path/to/new/engine --engine2 ../../Engine_13/engine \
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

    dotnet run -c Release -- --engine1 ../../Engine_13/engine --engine2 ../../Engine_13/engine \
        --nodes 10000 --random-plies 4 --games 20000 --concurrency 8 --option Threads=1 \
        --datagen positions.txt

writes one line per quiet position, `FEN;result` with the result from White's point
of view (`1.0`, `0.5`, `0.0`). A position is recorded when it is at least
`--skip-plies` (default 8) plies past the opening, the side to move is not in check,
and the engine's move is not a capture or promotion. `Engine_13`'s tuner
(`make tuner && ./tuner tune positions.txt`) then fits the evaluation to those
results; see `Engine_13/README.md`.

## Tuning search parameters (SPSA)

    dotnet run -c Release -- --engine1 ../../Engine_13/engine-spsa --spsa ../../Engine_13/spsa_params.txt \
        --spsa-out tuned_params.txt --games 20000 --tc 2+0.02 --concurrency 8 --option Threads=1

tunes the engine's search parameters the way Stockfish's Fishtest does. The
engine has to be the SPSA build (`make spsa`), which offers each parameter as a
UCI option. It plays itself in pairs of games (one opening, both colours): for
every pair each parameter is nudged up for one side and down for the other, in
random directions, and the pair's result moves every parameter towards the side
that did better. The nudges and steps shrink as the run goes on. The parameter
file lists `NAME start min max c_end` per line, `c_end` being the final nudge; the
current values are written to `--spsa-out` every 100 pairs and at the end, in the
same format, so a run can be continued from them.

## Options

Run without arguments for the list: engines, games, concurrency, `--tc`,
`--movetime` or `--nodes`, `--sprt`/`--no-sprt`, `--no-adjudication`,
`--random-plies`, `--option NAME=VALUE` (both engines), `--option1` / `--option2`
(one engine only, e.g. a different `Threads`, or `UCI_LimitStrength=true` and
`UCI_Elo=2400` for Stockfish), `--pgn`, `--datagen`, `--skip-plies`, `--report`,
`--spsa`, `--spsa-out`.
