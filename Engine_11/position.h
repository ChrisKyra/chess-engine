#pragma once

#include "bitboard.h"
#include "types.h"

#include <cstring>
#include <string>
#include <vector>

// Everything that make_move() destroys and unmake_move() has to put back.
struct StateInfo {
    Piece captured = NO_PIECE;
    Square ep_square = NO_SQUARE;
    int castling_rights = NO_CASTLING;
    int halfmove_clock = 0;
    int plies_from_null = 0;   // plies since the last null move; repetitions can't reach past one
    U64 key = 0;
};

class Position {
public:
    Position() { set_start_position(); }

    void set_start_position();
    bool set_from_fen(const std::string& fen);
    std::string fen() const;

    // ---- queries -------------------------------------------------------
    // The board as bitboards: every piece, every piece of one colour, every
    // piece of one type, one colour's pieces of one type, and one colour's
    // pieces of two types at once (rooks and queens, say, for slider attacks).
    U64 pieces() const { return occupied; }
    U64 pieces(Color c) const { return by_color[c]; }
    U64 pieces(PieceType pt) const { return by_type[pt]; }
    U64 pieces(Color c, PieceType pt) const { return by_color[c] & by_type[pt]; }
    U64 pieces(Color c, PieceType a, PieceType b) const {
        return by_color[c] & (by_type[a] | by_type[b]);
    }

    // The redundant piece array: "what is on this square" as one load, instead
    // of testing twelve bitboards.  king_square() reads the one king bit.
    Piece piece_on(Square s) const { return board[s]; }
    bool empty(Square s) const { return board[s] == NO_PIECE; }
    Square king_square(Color c) const { return lsb(pieces(c, KING)); }

    // The rest of the state a FEN records, plus the Zobrist key of the current
    // position and how many moves deep the game history is.
    Color side_to_move() const { return side; }
    Square ep_square() const { return state().ep_square; }
    int castling_rights() const { return state().castling_rights; }
    int halfmove_clock() const { return state().halfmove_clock; }
    int fullmove_number() const { return fullmove; }
    U64 key() const { return state().key; }
    int ply_from_root() const { return int(history.size()) - 1; }

    // ---- attacks -------------------------------------------------------

    // Every piece of either colour that attacks `s` for a given occupancy.
    U64 attackers_to(Square s, U64 occ) const;
    U64 attackers_to(Square s) const { return attackers_to(s, occupied); }

    bool attacked_by(Square s, Color by, U64 occ) const;
    bool attacked_by(Square s, Color by) const { return attacked_by(s, by, occupied); }

    // Same test, but with an explicit set of attacking pieces.  The legality
    // check needs this because a captured piece must be ignored even though the
    // destination square is still occupied.
    bool attacked_by(Square s, Color by, U64 occ, U64 enemies) const;

    bool in_check() const { return attacked_by(king_square(side), ~side); }
    bool in_check(Color c) const { return attacked_by(king_square(c), ~c); }

    // Enemy pieces currently giving check to the side to move.
    U64 checkers() const { return attackers_to(king_square(side)) & by_color[~side]; }

    // ---- move legality and execution -----------------------------------

    // A generated move is pseudo-legal; this rejects the ones that would leave
    // our own king under attack.
    bool is_legal(Move m) const;

    // Whether the side to move has a legal en passant capture onto `ep`.  Only
    // then is the square part of the position: same rule as for repetitions in
    // chess, and the only way identical positions get identical keys.
    bool can_capture_en_passant(Square ep) const;

    void make_move(Move m);
    void unmake_move(Move m);
    void make_null_move();
    void unmake_null_move();

    // ---- draw detection -------------------------------------------------
    bool is_repetition(int search_ply) const;
    bool is_fifty_move_draw() const { return state().halfmove_clock >= 100; }
    bool is_insufficient_material() const;

    // Does the side to move have a piece other than pawns and the king?
    // Null-move pruning is unsafe in zugzwang-prone positions without one.
    bool has_non_pawn_material(Color c) const {
        return pieces(c) & ~(pieces(c, PAWN) | pieces(c, KING));
    }

    // ---- static exchange evaluation --------------------------------------
    // Material won or lost by `m` once every recapture on its destination has
    // been played out, least valuable piece first (e.g. -875 for QxP defended
    // by a pawn).  Pins are ignored.
    int see(Move m) const;

    // ---- helpers --------------------------------------------------------
    Move parse_uci_move(const std::string& text) const;
    std::string to_string() const;

private:
    // The only three ways the board itself changes.  Each keeps the bitboards,
    // the occupancy and the piece array in step; nothing else touches them.
    void put_piece(Piece pc, Square s);
    void remove_piece(Square s);
    void move_piece(Square from, Square to);
    U64 compute_key() const;

    StateInfo& state() { return history.back(); }
    const StateInfo& state() const { return history.back(); }

    U64 by_type[PIECE_TYPE_NB] = {};
    U64 by_color[COLOR_NB] = {};
    U64 occupied = 0;
    Piece board[SQUARE_NB];

    Color side = WHITE;
    int fullmove = 1;
    std::vector<StateInfo> history;
};

namespace Zobrist {
void init();
extern U64 PSQ[PIECE_NB][SQUARE_NB];
extern U64 SIDE;
extern U64 CASTLING[16];
extern U64 EP_FILE[8];
}
