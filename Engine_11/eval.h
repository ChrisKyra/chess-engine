#pragma once

#include "position.h"

namespace Eval {

// Builds the piece-square lookup tables, the pawn-structure masks and the
// square-distance table.  Call once at start-up, after Bitboards::init().
void init();

// Empties the pawn hash table.  Not needed for correctness (every entry is
// verified against the actual pawns before it is used), but it keeps a new game
// from starting with another game's entries still in memory.
void clear();

// Whether the score fades as the fifty-move clock runs down ("Fifty Move
// Scaling").  On by default; turning it off restores the older behaviour, in
// which a position two moves from a draw by rule scored the same as a fresh one.
void set_fifty_move_scaling(bool on);

// Static evaluation in centipawns, from the point of view of the side to move.
// Positive means the side to move is better -- the convention negamax needs.
// Includes the fifty-move fade: evaluate(pos) == fifty_move_scale(evaluate_unscaled(pos), pos).
int evaluate(const Position& pos);

// The same evaluation before the fifty-move fade.  This is what the
// transposition table stores: its key does not include the fifty-move clock,
// so an evaluation that depended on the clock would be reused with the wrong one.
int evaluate_unscaled(const Position& pos);

// Fades `score` towards zero as the fifty-move clock runs down (when "Fifty
// Move Scaling" is on; otherwise returns it unchanged).
int fifty_move_scale(int score, const Position& pos);

// Rough material values, also used for move ordering and delta pruning.
extern const int PIECE_VALUE[PIECE_TYPE_NB];

} // namespace Eval

#ifdef EVAL_TRACE
#include <string>
#include <utility>
#include <vector>

// Only in the `tuner` build (make tuner): what Texel tuning needs to see inside
// the evaluation.  See tune.cpp.
namespace Eval::Tune {

// One tunable weight: a middlegame and an endgame value.  Some weights only
// exist in one phase (king shelter is middlegame-only, most passed-pawn terms
// endgame-only); the other half is left alone.
struct Param {
    std::string name;
    int mg = 0, eg = 0;
    bool tune_mg = true, tune_eg = true;
};

// Every tunable weight with its current value, indexed like the trace.
std::vector<Param> parameters();

// One position as the evaluation saw it.
struct Trace {
    std::vector<std::pair<int, double>> coefficients;  // weight index, White-minus-Black count
    int mg = 0, eg = 0;     // the middlegame and endgame totals before blending (White's view)
    int phase = 0;          // 24 with every piece on, 0 with none
    int scale = 64;         // endgame scale factor, out of 64
    int tempo = 0;          // the tempo bonus, from White's point of view
    int white_score = 0;    // the finished evaluation, from White's point of view
};
void trace(const Position& pos, Trace& out);

// C++ definitions of every weight with the given values, in the same form as
// eval.cpp, for pasting back (piece-square averages are moved into material).
std::string source(const std::vector<double>& mg, const std::vector<double>& eg);

} // namespace Eval::Tune
#endif
