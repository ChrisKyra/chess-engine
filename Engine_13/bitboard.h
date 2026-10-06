#pragma once

#include "types.h"

// ---------------------------------------------------------------------------
// A bitboard is 64 bits, one per square, with bit i corresponding to square i
// under the little-endian rank-file mapping declared in types.h.  Square A1 is
// therefore the least significant bit and H8 the most significant.
// ---------------------------------------------------------------------------

constexpr U64 FILE_A_BB = 0x0101010101010101ULL;
constexpr U64 FILE_B_BB = FILE_A_BB << 1;
constexpr U64 FILE_C_BB = FILE_A_BB << 2;
constexpr U64 FILE_D_BB = FILE_A_BB << 3;
constexpr U64 FILE_E_BB = FILE_A_BB << 4;
constexpr U64 FILE_F_BB = FILE_A_BB << 5;
constexpr U64 FILE_G_BB = FILE_A_BB << 6;
constexpr U64 FILE_H_BB = FILE_A_BB << 7;

constexpr U64 RANK_1_BB = 0xFFULL;
constexpr U64 RANK_2_BB = RANK_1_BB << 8;
constexpr U64 RANK_3_BB = RANK_1_BB << 16;
constexpr U64 RANK_4_BB = RANK_1_BB << 24;
constexpr U64 RANK_5_BB = RANK_1_BB << 32;
constexpr U64 RANK_6_BB = RANK_1_BB << 40;
constexpr U64 RANK_7_BB = RANK_1_BB << 48;
constexpr U64 RANK_8_BB = RANK_1_BB << 56;

constexpr U64 FILE_BB[8] = { FILE_A_BB, FILE_B_BB, FILE_C_BB, FILE_D_BB,
                             FILE_E_BB, FILE_F_BB, FILE_G_BB, FILE_H_BB };
constexpr U64 RANK_BB[8] = { RANK_1_BB, RANK_2_BB, RANK_3_BB, RANK_4_BB,
                             RANK_5_BB, RANK_6_BB, RANK_7_BB, RANK_8_BB };

// A bitboard holding one square, and the three ways of using one: test whether
// a square is in a set, add it, remove it.
constexpr U64 square_bb(Square s) { return 1ULL << int(s); }

inline bool test_bit(U64 b, Square s) { return b & square_bb(s); }
inline void set_bit(U64& b, Square s) { b |= square_bb(s); }
inline void clear_bit(U64& b, Square s) { b &= ~square_bb(s); }

// How many squares are in the set, and which is the lowest or highest of them.
// All three become single CPU instructions with -march=native.
inline int popcount(U64 b) { return __builtin_popcountll(b); }
inline Square lsb(U64 b) { return Square(__builtin_ctzll(b)); }
inline Square msb(U64 b) { return Square(63 ^ __builtin_clzll(b)); }

// Remove and return the least significant set bit.
inline Square pop_lsb(U64& b) {
    Square s = lsb(b);
    b &= b - 1;
    return s;
}

// ---------------------------------------------------------------------------
// Directional shifts.  Files are masked off first so that a piece on the A file
// cannot wrap around to the H file of the neighbouring rank.
// ---------------------------------------------------------------------------

constexpr U64 north(U64 b) { return b << 8; }
constexpr U64 south(U64 b) { return b >> 8; }
constexpr U64 east(U64 b) { return (b & ~FILE_H_BB) << 1; }
constexpr U64 west(U64 b) { return (b & ~FILE_A_BB) >> 1; }
constexpr U64 north_east(U64 b) { return (b & ~FILE_H_BB) << 9; }
constexpr U64 north_west(U64 b) { return (b & ~FILE_A_BB) << 7; }
constexpr U64 south_east(U64 b) { return (b & ~FILE_H_BB) >> 7; }
constexpr U64 south_west(U64 b) { return (b & ~FILE_A_BB) >> 9; }

// Pawn pushes and attacks, parameterised by colour.  Each one moves every pawn
// of that colour at once: one shift instead of a loop over the pawns.
template<Color C> constexpr U64 pawn_push(U64 b) { return C == WHITE ? north(b) : south(b); }
template<Color C> constexpr U64 pawn_attacks_left(U64 b) { return C == WHITE ? north_west(b) : south_west(b); }
template<Color C> constexpr U64 pawn_attacks_right(U64 b) { return C == WHITE ? north_east(b) : south_east(b); }

// One square forward for this colour, as a square-number offset.
constexpr int push_delta(Color c) { return c == WHITE ? 8 : -8; }

// ---------------------------------------------------------------------------
// Precomputed tables.  Call Bitboards::init() once at program start.
// ---------------------------------------------------------------------------

namespace Bitboards {

void init();

extern U64 PAWN_ATTACKS[COLOR_NB][SQUARE_NB];
extern U64 KNIGHT_ATTACKS[SQUARE_NB];
extern U64 KING_ATTACKS[SQUARE_NB];

// LINE_BB[a][b]   -- the whole file/rank/diagonal through a and b (0 if not aligned)
// BETWEEN_BB[a][b]-- the squares strictly between a and b (0 if not aligned)
extern U64 LINE_BB[SQUARE_NB][SQUARE_NB];
extern U64 BETWEEN_BB[SQUARE_NB][SQUARE_NB];

// A magic entry maps a blocker configuration to an index in a shared table.
struct Magic {
    U64 mask;       // relevant occupancy squares for this piece and square
    U64 magic;      // the multiplier that produces a collision-free index
    U64* attacks;   // pointer into the shared attack table
    unsigned shift; // 64 - number of relevant bits

    unsigned index(U64 occupied) const {
        return unsigned(((occupied & mask) * magic) >> shift);
    }
};

extern Magic ROOK_MAGICS[SQUARE_NB];
extern Magic BISHOP_MAGICS[SQUARE_NB];

} // namespace Bitboards

// ---------------------------------------------------------------------------
// Attack lookups.  For sliders the blocker set is hashed through the magic
// multiplier; a single multiply and shift replaces walking along each ray.
// ---------------------------------------------------------------------------

// The leaping pieces: nothing can block them, so their attack sets were worked
// out once at start-up and are a single table lookup here.
inline U64 pawn_attacks(Color c, Square s) { return Bitboards::PAWN_ATTACKS[c][s]; }
inline U64 knight_attacks(Square s) { return Bitboards::KNIGHT_ATTACKS[s]; }
inline U64 king_attacks(Square s) { return Bitboards::KING_ATTACKS[s]; }

// The sliding pieces: keep only the blockers that matter for this square, hash
// them with the magic multiplier, and read the answer out of the table.  A
// queen is simply a rook and a bishop on the same square.
inline U64 rook_attacks(Square s, U64 occupied) {
    const Bitboards::Magic& m = Bitboards::ROOK_MAGICS[s];
    return m.attacks[m.index(occupied)];
}

inline U64 bishop_attacks(Square s, U64 occupied) {
    const Bitboards::Magic& m = Bitboards::BISHOP_MAGICS[s];
    return m.attacks[m.index(occupied)];
}

inline U64 queen_attacks(Square s, U64 occupied) {
    return rook_attacks(s, occupied) | bishop_attacks(s, occupied);
}

// The attack set of whichever piece type is asked for, chosen at run time.
// Pawns are missing on purpose: their attacks depend on the colour, so they go
// through pawn_attacks() instead.
inline U64 attacks_of(PieceType pt, Square s, U64 occupied) {
    switch (pt) {
        case KNIGHT: return knight_attacks(s);
        case BISHOP: return bishop_attacks(s, occupied);
        case ROOK:   return rook_attacks(s, occupied);
        case QUEEN:  return queen_attacks(s, occupied);
        case KING:   return king_attacks(s);
        default:     return 0ULL;
    }
}

// The alignment tables as functions: the squares strictly between two squares,
// and the whole line through them.  Both are empty when the squares do not
// share a rank, file or diagonal.
inline U64 between_bb(Square a, Square b) { return Bitboards::BETWEEN_BB[a][b]; }
inline U64 line_bb(Square a, Square b) { return Bitboards::LINE_BB[a][b]; }

// True when the three squares lie on a common rank, file or diagonal.
inline bool aligned(Square a, Square b, Square c) {
    return line_bb(a, b) & square_bb(c);
}

// Prints a bitboard as an 8x8 grid of dots and crosses, for debugging.
void print_bitboard(U64 b);
