# Chess Engine

A C++ chess engine and a C# desktop GUI to play it and run engine matches.
The GitHub repository holds the latest engine only; older versions stay on the
development machine (listed below, ignored by git) and are described in the
version history of `Engine_10/README.md`.

```
Chess Engine/
├── Engine_11/             next engine     — "Bitboard Engine 11" (in development: evaluation tuning)
├── Engine_10/             current engine  — "Bitboard Engine 10"
├── Engine_9_Repetition/   previous engine — "Bitboard Engine 9 (Repetition)", kept to test against (local only)
├── Engine_8_Contempt/     older engine    — "Bitboard Engine 8 (Contempt)" (local only)
├── gui/                   C# / Avalonia GUI: play, analyse, and run Engine vs Engine matches
└── tools/match-runner/    command-line matches (SPRT, adjudication) and tuning data generation
```

## Engines

    cd Engine_10               # or Engine_9_Repetition, Engine_8_Contempt
    make          # build ./engine
    make test     # perft correctness suite -- run this after any change
    make bench    # node-count benchmark, to compare versions
    ./engine      # interactive UCI mode

Engine 10 is Engine 9 with four search improvements: static exchange pruning of
losing moves, continuation and capture history for move ordering, singular
extensions, and time management that spends more when the best move is
unsettled. Each was tested against the step before it, and the finished engine
beat Engine 9 by +135 ± 28 Elo over 400 games at 8 s + 0.08 s. An "improving"
test was tried and dropped because it lost Elo. `Engine_10/README.md` has the
results, what changed in every version, the architecture, and a reference for
every function.

Engine 9 is Engine 8 with four fixes: repetitions found reliably (en passant
hashing), checkmate beating the fifty-move rule, a transposition table that no
longer mixes fifty-move clocks, and no contempt while analysing.

To start Engine 11, copy `Engine_10` to a new folder, change `ENGINE_NAME` in
`uci.cpp`, and keep Engine 10 as the one to test against.

## GUI

    cd gui
    dotnet run --project src/ChessGui    # build and start
    ./build-mac-app.sh                   # Chess.app on the Desktop, with Engine 10 bundled

On the Engines tab, Load... two engines, e.g. `Engine_10/engine` and
`Engine_9_Repetition/engine` (build them first). The Match tab plays one against the
other from 293 openings at a chosen time control (a clock, a fixed time or node
count per move, or the engine's own choice) and shows the score, an Elo estimate,
an SPRT that can stop the match once it has an answer, each engine's threads, time,
nodes per move and nodes per second, and how the games ended. See `gui/README.md`.

Build output (`engine`, `*.o`, `gui/**/bin`, `gui/**/obj`) is not kept here; the
commands above recreate it.
