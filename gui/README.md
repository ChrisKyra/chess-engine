# Chess GUI (C# / Avalonia)

A desktop app for playing against your own chess engine. It talks to engines
over **UCI**, the standard text protocol on stdin/stdout, so any engine that
speaks UCI plugs in: the C++ engine in `../Engine_13`, one you write later, or
Stockfish for comparison.

Runs on macOS, Windows and Linux (.NET 10 + Avalonia 11).

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). If you
installed it with `dotnet-install.sh` into `~/.dotnet`, put it on your PATH first:

```sh
export PATH="$HOME/.dotnet:$PATH"
```

```sh
dotnet run --project src/ChessGui        # start the app
dotnet test                              # rules + UCI parsing tests (perft included)
```

After changing the GUI's code, `dotnet run` rebuilds it before starting. If you
use Chess.app, quit the running copy first (⌘Q) and run `./build-mac-app.sh`
again: opening the app while an old copy is still running only brings the old
copy to the front.

### Double-clickable Mac app

```sh
./build-mac-app.sh                # creates ~/Desktop/Chess.app
./build-mac-app.sh /Applications  # or put it somewhere else
```

This builds the engine (`../Engine_13`; change `ENGINE_DIR` in the
script to bundle another engine), publishes the GUI self-contained (no .NET needed to run
it), and bundles the engine inside the app, which loads it automatically when
you haven't chosen another engine. Rerun the script after changing the GUI or
the engine to refresh the app.

## Playing against an engine

The GUI runs up to two engines at once, **Engine 1** and **Engine 2**.

1. Build the engine (for example `make -C ../Engine_13`).
2. On the **Engines** tab, click **Load…** under Engine 1 or Engine 2 and pick
   the engine executable. For a script-based engine, choose the interpreter
   (for example `/usr/bin/python3`) and put the script path in
   *Command-line arguments*.
3. On the **Game** tab, choose who plays **White** and **Black** (Human,
   Engine 1 or Engine 2).
4. Press **Start**. Nothing moves and no engine calculates (not even analysis)
   until you do. **New game** and **Set position** only set up the board.
5. **Quit** stops the game, or a running match, and every engine calculation.
   Each engine is sent `stop`; one that is still calculating half a second later
   (an engine that ignores `stop` while searching) is killed and started again.
   Pressing Start after Quit begins a fresh game from the same starting position.

Outside matches the GUI doesn't set how long engines think: it sends a plain `go`
and each engine decides for itself (Engines 9 to 12 think for up to 500 ms per move on a
plain `go`). Matches have their own time control (see below). Engines can't be loaded, reloaded or unloaded
while a game or match is in progress, so an engine can't change mid-game.

Typical setups:

- **You vs your engine, with Stockfish's opinion:** Engine 1 = your engine,
  Engine 2 = Stockfish with **Analyse when not playing a move** ticked; set
  Black to Engine 1. The Evaluation card shows both engines' scores and lines.
- **Engine vs engine:** White = Engine 1, Black = Engine 2. **Swap sides**
  exchanges colours, **Pause** stops the match.

Analysis sends `go infinite` and relies on `stop` to end it, so only turn it on
for engines that honour `stop` (Stockfish does). An engine that is also playing
analyses only while it's the opponent's turn.

The engine paths, arguments, analysis switches, players and changed engine
options are saved and restored on the next launch.

### Using Stockfish

Download Stockfish from [stockfishchess.org](https://stockfishchess.org/download/),
then load its executable (e.g. `stockfish-macos-universal`) into an engine slot.
macOS blocks programs downloaded from the internet that aren't notarized; if
loading fails with "exit code 137", allow it once in Terminal:

```sh
xattr -d com.apple.quarantine /path/to/stockfish-macos-universal
```

Stockfish treats a plain `go` as "search until stopped", so on its own it never
moves in this GUI. Load `stockfish/stockfish-limited` instead of the Stockfish
binary: it's a small launcher that turns `go` (and `go infinite`) into
`go movetime 1000`, so Stockfish thinks for 1 second per move and per analysis.
Change `MOVE_TIME_MS` in that file for a different time. Settings are stored at
`~/Library/Application Support/ChessGui/settings.json` on macOS
(`%LOCALAPPDATA%\ChessGui` on Windows, `~/.local/share/ChessGui` on Linux).

**Stockfish's strength.** Stockfish advertises `UCI_Elo` (1320–3190). On the
Engines tab, open **Engine options**, pick Stockfish's slot, and drag the
**UCI_Elo** slider (steps of 10; click on the track or use the arrow keys too).
The value is sent when the slider stops, and moving it also ticks
**UCI_LimitStrength**, without which Stockfish ignores the Elo; untick that box to
play at full strength again. Both are saved like any other option, so match games
use them as well. Stockfish calibrated `UCI_Elo` at much longer time controls
than `stockfish-limited`'s fraction of a second per move, so at that speed it
plays weaker than the number says. Any engine that has a `UCI_Elo` option gets
the same slider.

While developing an engine, **Reload** restarts it after a rebuild, and the
**UCI log** shows every line sent and received (stderr too), with a box for
sending raw commands such as `d`.

Other controls: drag or click to move, **Undo** takes back your move and the
engine's reply, **Engine move** makes the engine play the side to move (or,
while it thinks, sends `stop`), **Pause** stops the engine from moving,
**Flip** (`F`), **New game** (`⌘N`/`Ctrl+N`), **Set position** from a FEN, and
**Copy FEN** / **Copy PGN**. The arrow shows the engine's current best move
while it thinks; the score is always from White's point of view.

With no engine loaded you can move for both sides, so the GUI is usable before
the engine exists.

## Engine matches

The **Match** tab plays a series of games between Engine 1 and Engine 2 (1000
by default) and keeps score: wins, losses and draws for each engine, the score
percentage, Engine 1's results with White and with Black, and an Elo difference
estimate with a 95% confidence margin.

**Time control.** Chosen on the Match tab, sent with every `go`:

| Setting | Engines receive | Use it for |
|---|---|---|
| Clock (default 10 s + 0.1 s) | `go wtime .. btime .. winc .. binc ..` | testing changes, time management included |
| Time per move | `go movetime N` | a fixed budget per move, e.g. "how many nodes in 0.5 s" |
| Nodes per move | `go nodes N` | the same search on any machine, however busy (hides speed differences) |
| Engine decides | plain `go` | the old behaviour |

With a clock the GUI keeps both clocks: the time from sending `go` to reading
`bestmove` (measured on the thread that reads the engine's output, so a busy
window can't add to it) comes off the mover's clock, then the increment is added.
An engine whose clock goes below zero loses on time, or draws if the opponent has
no mating material. The clocks of the game on the board are shown beside the
player names, and the PGN gets a `TimeControl` tag.

**Captured pieces.** While a match game is on the board, a panel to its left
shows, next to each player's side, the pieces that player has captured (grouped
by kind, cheapest first) and, for whoever is ahead in material, by how many pawns
("+3"), counting pawn 1, knight and bishop 3, rook 5, queen 9. The lead is counted
from the pieces on the board, so a promoted pawn counts as what it became.

**SPRT.** Every match runs a sequential probability ratio test on the results,
the way Fishtest and fastchess test engine changes. H0 says Engine 1 is at most
*H0* Elo stronger, H1 that it is at least *H1* Elo stronger (default 0 and 10),
with 5% error either way. Games are counted in pairs (each opening from both
sides, scoring 0, ½, 1, 1½ or 2 for Engine 1), which cancels out the advantage an
opening gives one colour. The Results card shows the log-likelihood ratio, its
bounds (±2.94), the decision and the pair counts. With *Stop the match when the
SPRT decides* ticked (the default), the number of games is a maximum: the match
stops as soon as H0 or H1 is accepted, abandoning games still in progress.

Two more cards follow:

- **Engine speed**: per engine, its `Threads` option, the number of moves, and
  the mean time per move, nodes per move and nodes per second over every move it
  played in the match, from the `nodes` and `time` in its last `info` line before
  each `bestmove`. A warning appears when the match would run more search threads
  at once (games at once × the larger `Threads`) than the computer has
  performance cores, because then the engines slow each other down. On an Apple M4
  that is 4.
- **How games ended**: decisive games (checkmate, plus losses on time or by an
  illegal move when they happen) and draws (repetition, fifty-move rule, plus
  stalemate and insufficient material when they happen), each as a count and a
  share of all games, then the average game length, overall, decisive and drawn.

- Colours alternate every game. Games start from a list of 293 well-known
  openings (main lines and established sidelines of the Ruy Lopez, Italian,
  Sicilian, French, Caro-Kann, Queen's Gambit, Slav, the Indian defences, the
  English, Réti and more, in `src/ChessCore/Openings.cs`), and each opening is
  played twice so both engines get it as White. A match uses each opening once
  until it has played 586 games; only longer matches come back to the start of
  the list.
- *Random moves after the opening* (default 0) adds seeded random legal moves to
  each pair's opening. They can be blunders, which decide the game instead of the
  engines, so only use 1 or 2 for matches longer than 586 games.
- Every match game runs on its own pair of engine processes, started from the
  loaded engines' programs with the same options; the loaded engines sit the match
  out. *Games at once* (default 1) plays that many games in parallel, each with its
  own pair of processes. Only the first game is shown on the board; the others run
  out of sight and appear in the results as they finish, so games can complete out
  of order. Only compare matches that were run with the same games at once and
  threads.
- Players and engines are locked during a match, background analysis is off, and
  Pause and Engine move are disabled (match games run on clocks). An engine that
  plays an illegal move loses that game; an engine that crashes stops the match.
- Every finished game is appended to a PGN file in the `matches` folder next to
  `settings.json`; **Save games as PGN…** copies it elsewhere.

### If engines won't load in Chess.app

`build-mac-app.sh` signs the app ad hoc, so every rebuild is a new app to macOS's
privacy checks. The first time the rebuilt app starts an engine on the Desktop,
macOS asks whether "Chess" may access files in your Desktop folder; until you
answer, the engine hangs and the GUI reports that it "did not answer 'uci'". Allow
it, then press **Reload** on the Engines tab. (If you denied it, enable it under
System Settings → Privacy & Security → Files and Folders.) macOS also ships its
own Chess app, which is why this one can show up as "Chess 2" in Launchpad.

## What your engine needs to implement

The GUI uses only this part of UCI:

| GUI sends | Engine must reply |
|---|---|
| `uci` | `id name <name>`, optionally `id author <you>` and `option ...` lines, then `uciok` |
| `isready` | `readyok` (at any time, including right after `ucinewgame` or `setoption`) |
| `setoption name <name> value <value>` | nothing |
| `ucinewgame` | nothing |
| `position startpos moves e2e4 e7e5 ...` or `position fen <fen> moves ...` | nothing |
| `go` (no limits: the engine decides how long to think) | any number of `info ...` lines, then exactly one `bestmove <move>` |
| `go infinite` (only for engines with *Analyse* ticked) | `info ...` lines until `stop` |
| `stop` | `bestmove <move>` as soon as possible |
| `quit` | exit |

- A plain `go` must end on its own: the engine picks its depth or time and then answers `bestmove`. An engine that treats a plain `go` as "search forever" (Stockfish does) never moves; use **Engine move** / *Move now* to send it `stop`.
- Moves use long algebraic notation: `e2e4`, `e1g1` (castling), `e7e8q` (promotion).
- **Flush stdout after every line** (`std::endl` or `std::cout.flush()`), or the GUI will wait forever.
- `info` is optional but powers the analysis panel:
  `info depth 12 score cp 34 nodes 600784 nps 4730582 time 127 pv e7e5 g1f3`.
  Scores are from the side to move's point of view; use `score mate 3` for mates.
- Lines the GUI doesn't understand are ignored (they still appear in the log). Debug output is best sent to stderr.
- If the engine plays an illegal move, the GUI pauses it and shows the move and FEN instead of playing it.
- The GUI never sends a new `go` until the previous one has been answered with `bestmove`, so a single-threaded engine that ignores `stop` still works; it just can't be interrupted.

A minimal C++ main loop that satisfies the contract:

```cpp
#include <iostream>
#include <sstream>
#include <string>

int main() {
    std::string line;
    while (std::getline(std::cin, line)) {
        std::istringstream in(line);
        std::string cmd;
        in >> cmd;
        if (cmd == "uci") {
            std::cout << "id name MyEngine\nid author Me\nuciok" << std::endl;
        } else if (cmd == "isready") {
            std::cout << "readyok" << std::endl;
        } else if (cmd == "ucinewgame") {
            // clear hash tables, etc.
        } else if (cmd == "position") {
            // set up the board from "startpos" or "fen ...", then play the "moves ..."
        } else if (cmd == "go") {
            // decide how long to think, search, print "info ..." lines as you go
            std::cout << "bestmove e7e5" << std::endl;
        } else if (cmd == "quit") {
            break;
        }
    }
}
```

## Project layout

```
ChessGui.slnx
src/ChessCore/          UI-free library
  Position.cs           board, FEN, legal move generation, draw rules
  San.cs, Game.cs       notation, move history, results, PGN
  Uci/UciEngine.cs      engine process + protocol client
  Uci/UciMessages.cs    info/option parsing, go limits
  Uci/SearchSpeed.cs    mean nodes per second over a match
  Openings.cs           opening list for matches
  MatchScore.cs         win/draw/loss tally and Elo estimate
  MatchTimeControl.cs   match time controls and the game clock
  Sprt.cs               sequential probability ratio test on game pairs
  GameEndTally.cs       how match games ended, and their length
src/ChessGui/           Avalonia desktop app
  BoardView.cs          board rendering, click/drag input, promotion picker
  PieceArt.cs           vector piece shapes
  GameController.cs     turn logic, engine searches and analysis, clocks
  EngineSlot.cs         one of the two engines and its latest output
  MatchRun.cs           an engine match in progress and its results
  MatchWorker.cs        one match game, its clocks and the two engines playing it
  MainWindow.axaml(.cs) layout and side panel
tests/ChessCore.Tests/  perft, SAN/FEN/result rules, UCI parsing
```
