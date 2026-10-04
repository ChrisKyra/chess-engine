#include "bitboard.h"

#include <cstdio>

namespace Bitboards {

U64 PAWN_ATTACKS[COLOR_NB][SQUARE_NB];
U64 KNIGHT_ATTACKS[SQUARE_NB];
U64 KING_ATTACKS[SQUARE_NB];
U64 LINE_BB[SQUARE_NB][SQUARE_NB];
U64 BETWEEN_BB[SQUARE_NB][SQUARE_NB];

Magic ROOK_MAGICS[SQUARE_NB];
Magic BISHOP_MAGICS[SQUARE_NB];

namespace {

// Shared backing storage for the magic lookups.  A rook needs at most 12
// relevant occupancy bits (4096 entries) and a bishop at most 9 (512), but the
// per-square requirement varies, so the tables are packed end to end.
U64 ROOK_TABLE[102400];
U64 BISHOP_TABLE[5248];

const int ROOK_DIRS[4][2] = { {0, 1}, {0, -1}, {1, 0}, {-1, 0} };
const int BISHOP_DIRS[4][2] = { {1, 1}, {1, -1}, {-1, 1}, {-1, -1} };

// Walk outward from `sq` in each direction, stopping on (and including) the
// first occupied square.  This is the slow, obviously-correct reference used to
// build the tables; the engine never calls it during search.
U64 sliding_attacks(Square sq, U64 occupied, const int dirs[4][2]) {
    U64 attacks = 0;
    for (int d = 0; d < 4; ++d) {
        int f = file_of(sq) + dirs[d][0];
        int r = rank_of(sq) + dirs[d][1];
        while (f >= 0 && f <= 7 && r >= 0 && r <= 7) {
            Square target = make_square(f, r);
            attacks |= square_bb(target);
            if (occupied & square_bb(target)) break;
            f += dirs[d][0];
            r += dirs[d][1];
        }
    }
    return attacks;
}

// The squares whose occupancy actually changes the attack set.  Board edges are
// excluded: a blocker on the far edge cannot block anything behind it, so
// including it would only double the table size for no benefit.
U64 relevant_occupancy(Square sq, const int dirs[4][2]) {
    U64 edges = ((RANK_1_BB | RANK_8_BB) & ~RANK_BB[rank_of(sq)])
              | ((FILE_A_BB | FILE_H_BB) & ~FILE_BB[file_of(sq)]);
    return sliding_attacks(sq, 0ULL, dirs) & ~edges;
}

// Enumerate the `index`-th subset of `mask` by distributing the bits of index
// across the set bits of the mask.
U64 subset_of(unsigned index, U64 mask) {
    U64 subset = 0;
    while (mask) {
        Square s = pop_lsb(mask);
        if (index & 1) subset |= square_bb(s);
        index >>= 1;
    }
    return subset;
}

// xorshift64* -- deterministic so that the magics are identical on every run.
class Random {
public:
    explicit Random(U64 seed) : state(seed) {}
    U64 next() {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return state * 2685821657736338717ULL;
    }
    // Magic multipliers need few set bits: ANDing three randoms together
    // gives roughly 8 set bits, which is where usable magics live.
    U64 sparse() { return next() & next() & next(); }
private:
    U64 state;
};

// Find a multiplier that maps every blocker configuration for `sq` onto a
// distinct table slot.  Collisions are tolerated when both configurations
// happen to produce the same attack set ("constructive collisions").
void init_magics(const int dirs[4][2], Magic magics[SQUARE_NB], U64* table) {
    U64 occupancies[4096], references[4096];
    int epoch[4096] = {};
    int current_epoch = 0;
    Random rng(0x9E3779B97F4A7C15ULL);
    U64* section = table;

    for (int s = SQ_A1; s <= SQ_H8; ++s) {
        Square sq = Square(s);
        Magic& m = magics[s];

        m.mask = relevant_occupancy(sq, dirs);
        int bits = popcount(m.mask);
        m.shift = 64 - bits;
        m.attacks = section;

        int size = 1 << bits;
        for (int i = 0; i < size; ++i) {
            occupancies[i] = subset_of(unsigned(i), m.mask);
            references[i] = sliding_attacks(sq, occupancies[i], dirs);
        }

        while (true) {
            m.magic = rng.sparse();

            // A quick filter: a usable magic must scatter the high bits of the
            // masked occupancy into the top byte of the product.
            if (popcount((m.mask * m.magic) >> 56) < 6) continue;

            ++current_epoch;
            bool ok = true;
            for (int i = 0; i < size && ok; ++i) {
                unsigned idx = m.index(occupancies[i]);
                if (epoch[idx] < current_epoch) {
                    epoch[idx] = current_epoch;
                    m.attacks[idx] = references[i];
                } else if (m.attacks[idx] != references[i]) {
                    ok = false;
                }
            }
            if (ok) break;
        }
        section += size;
    }
}

} // namespace

void init() {
    // ---- leaping pieces: one table lookup, no blockers to consider --------
    for (int s = SQ_A1; s <= SQ_H8; ++s) {
        Square sq = Square(s);
        U64 b = square_bb(sq);

        PAWN_ATTACKS[WHITE][s] = north_west(b) | north_east(b);
        PAWN_ATTACKS[BLACK][s] = south_west(b) | south_east(b);

        // The knight's eight destinations, each masked so that a knight near a
        // file edge cannot wrap around to the other side of the board.
        U64 n = 0;
        n |= (b & ~FILE_H_BB) << 17;
        n |= (b & ~FILE_A_BB) << 15;
        n |= (b & ~(FILE_G_BB | FILE_H_BB)) << 10;
        n |= (b & ~(FILE_A_BB | FILE_B_BB)) << 6;
        n |= (b & ~(FILE_G_BB | FILE_H_BB)) >> 6;
        n |= (b & ~(FILE_A_BB | FILE_B_BB)) >> 10;
        n |= (b & ~FILE_H_BB) >> 15;
        n |= (b & ~FILE_A_BB) >> 17;
        KNIGHT_ATTACKS[s] = n;

        KING_ATTACKS[s] = north(b) | south(b) | east(b) | west(b)
                        | north_east(b) | north_west(b)
                        | south_east(b) | south_west(b);
    }

    // ---- sliding pieces --------------------------------------------------
    init_magics(ROOK_DIRS, ROOK_MAGICS, ROOK_TABLE);
    init_magics(BISHOP_DIRS, BISHOP_MAGICS, BISHOP_TABLE);

    // ---- alignment tables, used for pins and check evasion ---------------
    for (int a = SQ_A1; a <= SQ_H8; ++a) {
        for (int b = SQ_A1; b <= SQ_H8; ++b) {
            LINE_BB[a][b] = 0;
            BETWEEN_BB[a][b] = 0;
        }
        for (PieceType pt : { BISHOP, ROOK }) {
            for (int b = SQ_A1; b <= SQ_H8; ++b) {
                Square sa = Square(a), sb = Square(b);
                if (a == b) continue;
                if (!(attacks_of(pt, sa, 0ULL) & square_bb(sb))) continue;

                LINE_BB[a][b] = (attacks_of(pt, sa, 0ULL) & attacks_of(pt, sb, 0ULL))
                              | square_bb(sa) | square_bb(sb);
                BETWEEN_BB[a][b] = attacks_of(pt, sa, square_bb(sb))
                                 & attacks_of(pt, sb, square_bb(sa));
            }
        }
    }
}

} // namespace Bitboards

std::string square_name(Square s) {
    if (s == NO_SQUARE) return "-";
    return std::string(1, char('a' + file_of(s))) + char('1' + rank_of(s));
}

std::string Move::to_uci() const {
    if (is_none()) return "0000";
    std::string text = square_name(from()) + square_name(to());
    if (is_promotion()) text += " nbrq"[promotion_type()];
    return text;
}

void print_bitboard(U64 b) {
    for (int r = 7; r >= 0; --r) {
        std::printf("%d  ", r + 1);
        for (int f = 0; f <= 7; ++f)
            std::printf("%c ", (b & square_bb(make_square(f, r))) ? 'x' : '.');
        std::printf("\n");
    }
    std::printf("\n   a b c d e f g h\n\n");
}
