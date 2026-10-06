# Chess Engine

A C++ chess engine and a C# desktop GUI to play it and run engine matches.

```
Chess Engine/
├── Engine_13/             the engine  — "Bitboard Engine 13"
├── gui/                   C# / Avalonia GUI: play, analyse, and run Engine vs Engine matches
└── tools/                 command-line matches, data generation and search tuning
    ├── match-runner/
    └── finish-engine.sh
```

## Engine

    cd Engine_13
    make          # build ./engine
    make test     # perft correctness suite -- run this after any change
    make bench    # node-count benchmark
    make tuner    # build ./tuner, the evaluation tuner
    make spsa     # build ./engine-spsa, with the search parameters as UCI options
    ./engine      # interactive UCI mode

A UCI engine built on bitboards. It searches with principal variation search,
iterative deepening and aspiration windows, a shared transposition table and up to
several threads (Lazy SMP), with null-move, futility and late-move pruning, late
move reductions, singular extensions, history-based move ordering and a correction
history that adjusts the evaluation by pawn structure. The evaluation is tapered
between middlegame and endgame, with weights fitted to 1.5 million self-play
positions (Texel tuning), and it knows the result of common endings exactly: king
and pawn against king, the mating patterns against a lone king, and the standard
draws. The search parameters are tuned by SPSA self-play.

`Engine_13/README.md` explains in detail what the engine does and how, and has a
reference for every function.

## Testing and tuning

    cd tools/match-runner
    dotnet run -c Release -- --engine1 /path/to/new/engine --engine2 ../../Engine_13/engine \
        --tc 8+0.08 --concurrency 8 --sprt 0,10 --games 300 --option Threads=1

plays engine matches from the command line with the GUI's openings, clocks, SPRT
and adjudication; with `--datagen` it writes the positions the evaluation is tuned
on, and with `--spsa` it tunes the search parameters. See
`tools/match-runner/README.md`.

    tools/finish-engine.sh ENGINE_DIR BASELINE_ENGINE [stage...]

runs the steps that finish an engine: build, SPSA tuning, writing the tuned values
into the source, a tuned-against-untuned check and a match against a baseline
engine. The script's header lists the stages.

## GUI

    cd gui
    dotnet run --project src/ChessGui    # build and start
    ./build-mac-app.sh                   # Chess.app on the Desktop, with the engine bundled

On the Engines tab, Load... two engines, e.g. `Engine_13/engine` and Stockfish
(build them first). The Match tab plays one against the other from 293 openings at
a chosen time control (a clock, a fixed time or node count per move, or the
engine's own choice) and shows the score, an Elo estimate, an SPRT that can stop
the match once it has an answer, each engine's threads, time, nodes per move and
nodes per second, and how the games ended. See `gui/README.md`.

Build output (`engine`, `tuner`, `engine-spsa`, `*.o`, `*.d`, `gui/**/bin`, `gui/**/obj`)
is not kept here; the commands above recreate it.
