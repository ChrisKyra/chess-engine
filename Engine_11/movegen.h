#pragma once

#include "position.h"
#include "types.h"

// A fixed-capacity move list.  256 is comfortably above the largest number of
// legal moves reachable in a real chess position (the record is 218), so no
// allocation ever happens during search.
struct MoveList {
    ScoredMove moves[MAX_MOVES];
    int count = 0;

    // Appends a move with no ordering score; the search fills that in later.
    void add(Move m) {
        moves[count].move = m;
        moves[count].score = 0;
        ++count;
    }

    // Emptying, size questions, indexing, and begin()/end() so that the list
    // can be walked with a range-for loop.
    void clear() { count = 0; }
    int size() const { return count; }
    bool empty() const { return count == 0; }

    ScoredMove& operator[](int i) { return moves[i]; }
    const ScoredMove& operator[](int i) const { return moves[i]; }

    ScoredMove* begin() { return moves; }
    ScoredMove* end() { return moves + count; }
    const ScoredMove* begin() const { return moves; }
    const ScoredMove* end() const { return moves + count; }
};

enum GenType { GEN_ALL, GEN_CAPTURES };

// Pseudo-legal moves may leave the mover's own king in check.  They are cheaper
// to produce, and the search filters them with Position::is_legal().
void generate_pseudo_legal(const Position& pos, MoveList& list, GenType type = GEN_ALL);

// Fully legal moves, for the root, for perft and for interfacing with a GUI.
void generate_legal(const Position& pos, MoveList& list);
