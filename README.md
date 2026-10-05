# Chess Engine

A C++ chess engine and a C# desktop GUI to play it and run engine matches.
The GitHub repository holds the latest engine only; older versions stay on the
development machine and are described in the version history of
`Engine_12/README.md`.

```
Chess Engine/
├── Engine_12/             current engine  — "Bitboard Engine 12"
├── gui/                   C# / Avalonia GUI: play, analyse, and run Engine vs Engine matches
└── tools/match-runner/    command-line matches (SPRT, adjudication) and tuning data generation
```

## Engines

    cd Engine_12
    make          # build ./engine
    make test     # perft correctness suite -- run this after any change
    make bench    # node-count benchmark, to compare versions
    make tuner    # build ./tuner, the evaluation tuner
    ./engine      # interactive UCI mode

Engine 12 is Engine 11 with endgame knowledge: king and pawn against king solved
exactly (a table worked out at start-up), mating drives for a queen, a rook, or
bishop and knight against a lone king, the known draws with rook pawns and the
wrong-coloured bishop, a stalemate check, and drawish scaling when the defending
king blocks the pawn in rook or bishop endings. It mates with bishop and knight in
5 of 6 test positions (Engine 11: 4 of 6); in ordinary games it is level with
Engine 11 (+6 ± 25 Elo over 300 games), since such endings are rare.

Engine 11 tuned the evaluation: its ~480 weight pairs are fitted to the results of
1.5 million self-play positions (Texel tuning), with new terms for safe checks,
pawn storms, the long-diagonal bishop, trapped rooks, bad bishops, minor pieces
behind pawns and king protectors: +121 ± 31 Elo over Engine 10 for the tuning,
+42 ± 30 more for the terms. Engine 10 added four search improvements (+135 ± 28
over Engine 9). `Engine_12/README.md` has every version's changes and results, how
the tuning works and how to repeat it, the architecture, and a reference for every
function.

To start the next engine, copy `Engine_12` to a new folder, change `ENGINE_NAME`
in `uci.cpp`, and keep Engine 12 as the one to test against.

## Testing and tuning

    cd tools/match-runner
    dotnet run -c Release -- --engine1 /path/to/new/engine --engine2 ../../Engine_12/engine \
        --tc 8+0.08 --concurrency 8 --sprt 0,10 --games 300 --option Threads=1

plays engine matches from the command line with the GUI's openings, clocks, SPRT
and adjudication, and with `--datagen` writes the positions the evaluation is
tuned on. See `tools/match-runner/README.md`.

## GUI

    cd gui
    dotnet run --project src/ChessGui    # build and start
    ./build-mac-app.sh                   # Chess.app on the Desktop, with Engine 12 bundled

On the Engines tab, Load... two engines, e.g. `Engine_12/engine` and an older
version or Stockfish (build them first). The Match tab plays one against the
other from 293 openings at a chosen time control (a clock, a fixed time or node
count per move, or the engine's own choice) and shows the score, an Elo estimate,
an SPRT that can stop the match once it has an answer, each engine's threads, time,
nodes per move and nodes per second, and how the games ended. See `gui/README.md`.

Build output (`engine`, `tuner`, `*.o`, `gui/**/bin`, `gui/**/obj`) is not kept
here; the commands above recreate it.
