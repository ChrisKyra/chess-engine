#pragma once

#include <cstdint>
#include <string>

using U64 = uint64_t;

constexpr int MAX_MOVES = 256;
constexpr int MAX_PLY = 128;

// ---------------------------------------------------------------- colours

enum Color : int { WHITE, BLACK, COLOR_NB = 2 };

constexpr Color operator~(Color c) { return Color(c ^ 1); }

// ---------------------------------------------------------------- pieces

enum PieceType : int {
    PAWN, KNIGHT, BISHOP, ROOK, QUEEN, KING,
    PIECE_TYPE_NB = 6, NO_PIECE_TYPE = 6
};

enum Piece : int {
    W_PAWN, W_KNIGHT, W_BISHOP, W_ROOK, W_QUEEN, W_KING,
    B_PAWN, B_KNIGHT, B_BISHOP, B_ROOK, B_QUEEN, B_KING,
    NO_PIECE, PIECE_NB = 13
};

// Pieces are numbered as colour * 6 + type, so these three conversions are
// arithmetic rather than lookups: make_piece() puts a piece together, type_of()
// drops the colour, color_of() keeps only the colour.
constexpr Piece make_piece(Color c, PieceType pt) { return Piece(c * 6 + pt); }
constexpr PieceType type_of(Piece p) { return PieceType(p % 6); }
constexpr Color color_of(Piece p) { return Color(p / 6); }

// ---------------------------------------------------------------- squares
// Little-endian rank-file mapping: A1 = 0, B1 = 1, ... H8 = 63.

enum Square : int {
    SQ_A1, SQ_B1, SQ_C1, SQ_D1, SQ_E1, SQ_F1, SQ_G1, SQ_H1,
    SQ_A2, SQ_B2, SQ_C2, SQ_D2, SQ_E2, SQ_F2, SQ_G2, SQ_H2,
    SQ_A3, SQ_B3, SQ_C3, SQ_D3, SQ_E3, SQ_F3, SQ_G3, SQ_H3,
    SQ_A4, SQ_B4, SQ_C4, SQ_D4, SQ_E4, SQ_F4, SQ_G4, SQ_H4,
    SQ_A5, SQ_B5, SQ_C5, SQ_D5, SQ_E5, SQ_F5, SQ_G5, SQ_H5,
    SQ_A6, SQ_B6, SQ_C6, SQ_D6, SQ_E6, SQ_F6, SQ_G6, SQ_H6,
    SQ_A7, SQ_B7, SQ_C7, SQ_D7, SQ_E7, SQ_F7, SQ_G7, SQ_H7,
    SQ_A8, SQ_B8, SQ_C8, SQ_D8, SQ_E8, SQ_F8, SQ_G8, SQ_H8,
    NO_SQUARE, SQUARE_NB = 64
};

// Square arithmetic follows straight from the mapping: the rank is the square
// number divided by eight, the file is the remainder, make_square() puts the
// two back together, and flipping a square top to bottom is a single xor.
constexpr int rank_of(Square s) { return int(s) >> 3; }
constexpr int file_of(Square s) { return int(s) & 7; }
constexpr Square make_square(int file, int rank) { return Square(rank * 8 + file); }
constexpr Square flip_rank(Square s) { return Square(int(s) ^ 56); }

// Relative square/rank from a colour's point of view.
constexpr Square relative_square(Color c, Square s) { return c == WHITE ? s : flip_rank(s); }
constexpr int relative_rank(Color c, Square s) { return c == WHITE ? rank_of(s) : 7 - rank_of(s); }

std::string square_name(Square s);

// ---------------------------------------------------------------- castling

enum CastlingRight : int {
    NO_CASTLING = 0,
    WHITE_OO = 1, WHITE_OOO = 2,
    BLACK_OO = 4, BLACK_OOO = 8,
    ANY_CASTLING = 15
};

// ---------------------------------------------------------------- moves
//
// A move is packed into 16 bits:
//   bits  0-5   origin square
//   bits  6-11  destination square
//   bits 12-15  move type
//
// The move-type encoding is chosen so that bit 2 means "capture" and
// bit 3 means "promotion", which makes the predicates below single tests.

enum MoveType : int {
    QUIET = 0, DOUBLE_PUSH = 1, CASTLE_OO = 2, CASTLE_OOO = 3,
    CAPTURE = 4, EN_PASSANT = 5,
    PROMO_KNIGHT = 8, PROMO_BISHOP = 9, PROMO_ROOK = 10, PROMO_QUEEN = 11,
    PROMO_CAPTURE_KNIGHT = 12, PROMO_CAPTURE_BISHOP = 13,
    PROMO_CAPTURE_ROOK = 14, PROMO_CAPTURE_QUEEN = 15
};

class Move {
public:
    // Three ways to make a move: the empty one, one rebuilt from the 16 bits a
    // transposition table entry stored, and one built from its parts.
    constexpr Move() : data(0) {}
    constexpr explicit Move(uint16_t d) : data(d) {}
    constexpr Move(Square from, Square to, MoveType mt = QUIET)
        : data(uint16_t(uint16_t(from) | (uint16_t(to) << 6) | (uint16_t(mt) << 12))) {}

    // The three packed fields, unpacked again.
    constexpr Square from() const { return Square(data & 0x3F); }
    constexpr Square to() const { return Square((data >> 6) & 0x3F); }
    constexpr MoveType type() const { return MoveType(data >> 12); }

    // Questions about the move type, each one bit test or one comparison,
    // because of how the move-type numbers were chosen above.
    constexpr bool is_capture() const { return (data >> 12) & 4; }
    constexpr bool is_promotion() const { return (data >> 12) & 8; }
    constexpr bool is_castle() const { return type() == CASTLE_OO || type() == CASTLE_OOO; }
    constexpr bool is_en_passant() const { return type() == EN_PASSANT; }

    // Knight, bishop, rook or queen for a promoting move.
    constexpr PieceType promotion_type() const { return PieceType(KNIGHT + (data >> 12 & 3)); }

    // "No move": all sixteen bits zero, which no real move can be, since it
    // would mean a quiet move from a1 to a1.  raw() is the packed form, which
    // is what the transposition table stores.
    constexpr bool is_none() const { return data == 0; }
    constexpr uint16_t raw() const { return data; }

    constexpr bool operator==(const Move& m) const { return data == m.data; }
    constexpr bool operator!=(const Move& m) const { return data != m.data; }

    // Long algebraic notation, e.g. "e2e4", "e7e8q" -- the format UCI uses.
    std::string to_uci() const;

private:
    uint16_t data;
};

constexpr Move MOVE_NONE = Move();

// A move plus the ordering score assigned to it by the search.
struct ScoredMove {
    Move move;
    int score = 0;
};

// ---------------------------------------------------------------- scores

constexpr int VALUE_INFINITE = 32000;
constexpr int VALUE_MATE = 31000;
constexpr int VALUE_MATE_IN_MAX_PLY = VALUE_MATE - MAX_PLY;
constexpr int VALUE_DRAW = 0;

constexpr int mate_in(int ply) { return VALUE_MATE - ply; }
constexpr int mated_in(int ply) { return -VALUE_MATE + ply; }
