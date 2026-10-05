#pragma once

#include "position.h"
#include "types.h"

#include <cstdint>
#include <string>
#include <vector>

namespace Search {

// Largest transposition table the "Hash" option accepts, in megabytes.
constexpr int MAX_HASH_MB = 4096;

// Most threads the "Threads" option accepts.
constexpr int MAX_THREADS = 64;

// Largest "Contempt" the option accepts, in centipawns.
constexpr int MAX_CONTEMPT = 100;

// Every limit that is set applies; the search ends at whichever comes first.
struct Limits {
    int depth = 0;              // fixed depth, 0 means "no depth limit"
    int64_t movetime = 0;       // fixed milliseconds for this move
    int64_t time[COLOR_NB] = { 0, 0 };
    int64_t inc[COLOR_NB] = { 0, 0 };
    bool has_clock = false;     // "wtime" or "btime" was given (even as 0)
    int movestogo = 0;
    uint64_t nodes = 0;         // stop after about this many nodes, 0 means no limit
    int mate = 0;               // stop once a mate in this many moves is found
    bool infinite = false;      // think until "stop"
    bool ponder = false;        // think on the opponent's time until "ponderhit" or "stop"
    std::vector<Move> searchmoves;  // only these moves at the root (empty: all of them)
};

struct Result {
    Move best = MOVE_NONE;
    Move ponder = MOVE_NONE;
    int score = 0;
    int depth = 0;
    uint64_t nodes = 0;
};

// Allocate the transposition table (size in megabytes, clamped to
// 1..MAX_HASH_MB).  If the memory isn't available a smaller table is used and
// false is returned; hash_megabytes() tells the size actually in use.
bool init(size_t megabytes);
size_t hash_megabytes();

// Wipe the table and the history heuristics -- called on "ucinewgame" and by
// the "Clear Hash" option.
void clear();

// How many threads search at once ("Threads").  They all search the same
// position and share the transposition table: nobody is given a separate part
// of the tree.  Only call this between searches.
void set_threads(int count);
int threads();

// Time kept back from every move for the GUI to receive it ("Move Overhead").
void set_move_overhead(int milliseconds);

// How much the engine dislikes a draw, in centipawns ("Contempt").  A draw is
// scored as this much *against* the side that started the search, so a
// repetition is only chosen when everything else is worse than that.  Zero
// scores draws as dead level, the way every earlier version did.  Analysis
// ("go infinite") always uses zero, so the scores it shows are not skewed.
void set_contempt(int centipawns);
int contempt();

// Run iterative deepening and return the best move found.  The search ends when
// a limit is reached or request_stop() is called; depth 1 always completes, so
// there is a move to play even after an immediate stop.
Result think(Position& pos, const Limits& limits);

// The search runs on its own thread while the UCI loop keeps reading commands.
// These are safe to call from either thread.
void request_stop();
void clear_stop();
bool stop_requested();

// Pondering: while set, the search ignores its time limits.  ponderhit() starts
// the clock from that moment, so the move gets its full time budget.
void set_pondering(bool on);
void ponderhit();
bool pondering();

// Writes one line to stdout under a lock, so the search thread's output and the
// UCI loop's replies never interleave.
void print_line(const std::string& line);

} // namespace Search
