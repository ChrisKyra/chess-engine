#pragma once

#include "position.h"

#include <cstdint>

// Counts leaf nodes of the legal move tree to a given depth.  Comparing these
// numbers against published reference values is the standard way to prove a
// move generator correct: any error in castling, en passant, promotion or pin
// handling changes the count.
uint64_t perft(Position& pos, int depth);

// Per-move breakdown at the root, which is how you bisect a mismatch: compare
// against a known-good engine, descend into the move whose subtotal differs.
void perft_divide(Position& pos, int depth);

// Runs the standard test suite and reports pass/fail.
bool run_perft_suite();
