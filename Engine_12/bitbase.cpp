#include "bitbase.h"
#include "bitboard.h"

#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <vector>

namespace Bitbase {

namespace {

// Positions are stored with White as the side with the pawn, and the pawn on
// files a to d (the other wing is the mirror image).  The index packs the White
// king (6 bits), the Black king (6), the side to move (1), the pawn's file (2)
// and its rank, counted down from the seventh (3): 64 * 64 * 2 * 4 * 6 = 196,608.
constexpr int MAX_INDEX = 2 * 24 * 64 * 64;

int index(Color stm, Square black_king, Square white_king, Square pawn) {
    return int(white_king) | (int(black_king) << 6) | (int(stm) << 12)
         | (file_of(pawn) << 13) | ((6 - rank_of(pawn)) << 15);
}

// One bit per position: set when White wins.
uint32_t win_bits[MAX_INDEX / 32];

enum Result : uint8_t { INVALID = 0, UNKNOWN = 1, DRAW = 2, WIN = 4 };

int distance(Square a, Square b) {
    return std::max(std::abs(file_of(a) - file_of(b)), std::abs(rank_of(a) - rank_of(b)));
}

struct Entry {
    Color stm;
    Square king[COLOR_NB];
    Square pawn;
    Result result;

    // The result that can be read off the position itself, before any search:
    // impossible positions, a pawn that queens safely, and the defender
    // stalemated or able to take the pawn.
    explicit Entry(int idx) {
        king[WHITE] = Square(idx & 63);
        king[BLACK] = Square((idx >> 6) & 63);
        stm = Color((idx >> 12) & 1);
        pawn = make_square((idx >> 13) & 3, 6 - ((idx >> 15) & 7));

        const Square queening = Square(int(pawn) + 8);
        if (distance(king[WHITE], king[BLACK]) <= 1 || king[WHITE] == pawn || king[BLACK] == pawn
            || (stm == WHITE && (pawn_attacks(WHITE, pawn) & square_bb(king[BLACK])))) {
            result = INVALID;   // kings touching, a king on the pawn, or Black in check with White to move
        } else if (stm == WHITE && rank_of(pawn) == 6 && king[WHITE] != queening && king[BLACK] != queening
                   && (distance(king[BLACK], queening) > 1 || distance(king[WHITE], queening) == 1)) {
            result = WIN;       // the pawn promotes and the new queen cannot be taken
        } else if (stm == BLACK
                   && (!(king_attacks(king[BLACK]) & ~(king_attacks(king[WHITE]) | pawn_attacks(WHITE, pawn)))
                       || (king_attacks(king[BLACK]) & square_bb(pawn) & ~king_attacks(king[WHITE])))) {
            result = DRAW;      // stalemate, or the pawn is taken
        } else {
            result = UNKNOWN;
        }
    }

    // One step of the analysis: the side to move wins if one of its moves wins
    // (White) or draws if one of its moves draws (Black); otherwise, once every
    // move is known, the other result.
    Result classify(const std::vector<Entry>& db) const {
        const Color them = ~stm;
        const Result good = (stm == WHITE) ? WIN : DRAW;
        const Result bad = (stm == WHITE) ? DRAW : WIN;

        int seen = INVALID;
        U64 moves = king_attacks(king[stm]);
        while (moves) {
            const Square to = pop_lsb(moves);
            seen |= (stm == WHITE) ? db[index(them, king[BLACK], to, pawn)].result
                                   : db[index(them, to, king[WHITE], pawn)].result;
        }

        if (stm == WHITE) {
            // Pawn pushes; a push onto a king's square leads to an invalid entry,
            // which counts for nothing.
            if (rank_of(pawn) < 6)
                seen |= db[index(them, king[BLACK], king[WHITE], Square(int(pawn) + 8))].result;
            if (rank_of(pawn) == 1 && Square(int(pawn) + 8) != king[WHITE] && Square(int(pawn) + 8) != king[BLACK])
                seen |= db[index(them, king[BLACK], king[WHITE], Square(int(pawn) + 16))].result;
        }

        return (seen & good) ? good : (seen & UNKNOWN) ? UNKNOWN : bad;
    }
};

} // namespace

void init() {
    std::vector<Entry> db;
    db.reserve(MAX_INDEX);
    for (int idx = 0; idx < MAX_INDEX; ++idx) db.emplace_back(idx);

    // Keep resolving positions from the ones already known until nothing changes.
    bool changed = true;
    while (changed) {
        changed = false;
        for (Entry& e : db)
            if (e.result == UNKNOWN) {
                const Result r = e.classify(db);
                if (r != UNKNOWN) { e.result = r; changed = true; }
            }
    }

    std::fill(std::begin(win_bits), std::end(win_bits), 0u);
    for (int idx = 0; idx < MAX_INDEX; ++idx)
        if (db[idx].result == WIN) win_bits[idx / 32] |= 1u << (idx % 32);
}

bool kpk_win(Color strong, Square strong_king, Square pawn, Square weak_king, Color side_to_move) {
    // Seen from the strong side as White ...
    if (strong == BLACK) {
        strong_king = flip_rank(strong_king);
        pawn = flip_rank(pawn);
        weak_king = flip_rank(weak_king);
        side_to_move = ~side_to_move;
    }
    // ... and with the pawn on files a to d.
    if (file_of(pawn) > 3) {
        strong_king = Square(int(strong_king) ^ 7);
        pawn = Square(int(pawn) ^ 7);
        weak_king = Square(int(weak_king) ^ 7);
    }
    const int idx = index(side_to_move, weak_king, strong_king, pawn);
    return win_bits[idx / 32] & (1u << (idx % 32));
}

} // namespace Bitbase
