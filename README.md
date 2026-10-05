# Chess Engine

A C++ chess engine and a C# desktop GUI to play it and run engine matches.
The GitHub repository holds the latest engine only; older versions stay on the
development machine and are described in the version history of
`Engine_11/README.md`.

```
Chess Engine/
├── Engine_11/             current engine  — "Bitboard Engine 11"
├── gui/                   C# / Avalonia GUI: play, analyse, and run Engine vs Engine matches
└── tools/match-runner/    command-line matches (SPRT, adjudication) and tuning data generation
```

## Engines

    cd Engine_11
    make          # build ./engine
    make test     # perft correctness suite -- run this after any change
    make bench    # node-count benchmark, to compare versions
    make tuner    # build ./tuner, the evaluation tuner
    ./engine      # interactive UCI mode

Engine 11 is Engine 10 with a tuned evaluation. Its ~480 weight pairs, which used
to be hand-picked, are fitted to the results of 1.5 million self-play positions
(Texel tuning), and it has new terms: safe checks, pawn storms, the long-diagonal
bishop, trapped rooks, bad bishops, minor pieces behind pawns and king
protectors. The tuning alone beat Engine 10 by +121 ± 31 Elo over 400 games at
8 s + 0.08 s, and the new terms with a second tuning round added +42 ± 30.
`Engine_11/README.md` has how the tuning works and how to repeat it, the results,
what changed in every version, the architecture, and a reference for every
function.

Engine 10 is Engine 9 with four search improvements (static exchange pruning,
continuation and capture history, singular extensions, time management): +135 ± 28
Elo over Engine 9.

To start the next engine, copy `Engine_11` to a new folder, change `ENGINE_NAME`
in `uci.cpp`, and keep Engine 11 as the one to test against.

## Testing and tuning

    cd tools/match-runner
    dotnet run -c Release -- --engine1 ../../Engine_12/engine --engine2 ../../Engine_11/engine \
        --tc 8+0.08 --concurrency 8 --sprt 0,10 --games 300 --option Threads=1

plays engine matches from the command line with the GUI's openings, clocks, SPRT
and adjudication, and with `--datagen` writes the positions the evaluation is
tuned on. See `tools/match-runner/README.md`.

## GUI

    cd gui
    dotnet run --project src/ChessGui    # build and start
    ./build-mac-app.sh                   # Chess.app on the Desktop, with Engine 11 bundled

On the Engines tab, Load... two engines, e.g. `Engine_11/engine` and
`Engine_10/engine` (build them first). The Match tab plays one against the
other from 293 openings at a chosen time control (a clock, a fixed time or node
count per move, or the engine's own choice) and shows the score, an Elo estimate,
an SPRT that can stop the match once it has an answer, each engine's threads, time,
nodes per move and nodes per second, and how the games ended. See `gui/README.md`.

Build output (`engine`, `tuner`, `*.o`, `gui/**/bin`, `gui/**/obj`) is not kept
here; the commands above recreate it.
