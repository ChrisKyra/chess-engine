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
