#include "movegen.h"

namespace {

// ---------------------------------------------------------------------------
// Pawns need their own routine because they move and capture differently, they
// have a double push from the home rank, they promote, and they can capture en
// passant.  Working on whole bitboards means every pawn of one colour is
// handled with the same few shifts.
// ---------------------------------------------------------------------------

template<Color Us, GenType Type>
void generate_pawn_moves(const Position& pos, MoveList& list) {
    constexpr Color Them = ~Us;
    constexpr U64 SeventhRank = (Us == WHITE) ? RANK_7_BB : RANK_2_BB;
    constexpr U64 ThirdRank = (Us == WHITE) ? RANK_3_BB : RANK_6_BB;
    constexpr int Up = (Us == WHITE) ? 8 : -8;
    constexpr int UpLeft = (Us == WHITE) ? 7 : -9;
    constexpr int UpRight = (Us == WHITE) ? 9 : -7;

    const U64 pawns = pos.pieces(Us, PAWN);
    const U64 candidates = pawns & ~SeventhRank;   // cannot promote
    const U64 promoting = pawns & SeventhRank;
    const U64 empties = ~pos.pieces();
    const U64 enemies = pos.pieces(Them);

    // ---- quiet pushes ----------------------------------------------------
    if (Type != GEN_CAPTURES) {
        U64 single = pawn_push<Us>(candidates) & empties;
        U64 doubles = pawn_push<Us>(single & ThirdRank) & empties;

        while (single) {
            Square to = pop_lsb(single);
            list.add(Move(Square(int(to) - Up), to, QUIET));
        }
        while (doubles) {
            Square to = pop_lsb(doubles);
            list.add(Move(Square(int(to) - 2 * Up), to, DOUBLE_PUSH));
        }
    }

    // ---- promotions ------------------------------------------------------
    if (promoting) {
        U64 push = pawn_push<Us>(promoting) & empties;
        U64 left = pawn_attacks_left<Us>(promoting) & enemies;
        U64 right = pawn_attacks_right<Us>(promoting) & enemies;

        while (push) {
            Square to = pop_lsb(push);
            Square from = Square(int(to) - Up);
            if (Type != GEN_CAPTURES) {
                list.add(Move(from, to, PROMO_KNIGHT));
                list.add(Move(from, to, PROMO_BISHOP));
                list.add(Move(from, to, PROMO_ROOK));
            }
            list.add(Move(from, to, PROMO_QUEEN));
        }
        while (left) {
            Square to = pop_lsb(left);
            Square from = Square(int(to) - UpLeft);
            if (Type != GEN_CAPTURES) {
                list.add(Move(from, to, PROMO_CAPTURE_KNIGHT));
                list.add(Move(from, to, PROMO_CAPTURE_BISHOP));
                list.add(Move(from, to, PROMO_CAPTURE_ROOK));
            }
            list.add(Move(from, to, PROMO_CAPTURE_QUEEN));
        }
        while (right) {
            Square to = pop_lsb(right);
            Square from = Square(int(to) - UpRight);
            if (Type != GEN_CAPTURES) {
                list.add(Move(from, to, PROMO_CAPTURE_KNIGHT));
                list.add(Move(from, to, PROMO_CAPTURE_BISHOP));
                list.add(Move(from, to, PROMO_CAPTURE_ROOK));
            }
            list.add(Move(from, to, PROMO_CAPTURE_QUEEN));
        }
    }

    // ---- ordinary captures ----------------------------------------------
    U64 left = pawn_attacks_left<Us>(candidates) & enemies;
    U64 right = pawn_attacks_right<Us>(candidates) & enemies;

    while (left) {
        Square to = pop_lsb(left);
        list.add(Move(Square(int(to) - UpLeft), to, CAPTURE));
    }
    while (right) {
        Square to = pop_lsb(right);
        list.add(Move(Square(int(to) - UpRight), to, CAPTURE));
    }

    // ---- en passant ------------------------------------------------------
    // A pawn of ours can capture onto the en-passant square exactly when it
    // stands on a square that an enemy pawn placed there would attack.
    Square ep = pos.ep_square();
    if (ep != NO_SQUARE) {
        U64 takers = candidates & pawn_attacks(Them, ep);
        while (takers) {
            Square from = pop_lsb(takers);
            list.add(Move(from, ep, EN_PASSANT));
        }
    }
}

// ---------------------------------------------------------------------------
// Knights, bishops, rooks, queens and the king all reduce to "look up the
// attack set, mask out squares occupied by our own pieces, emit one move per
// remaining bit".  For sliders the lookup goes through the magic tables.
// ---------------------------------------------------------------------------

template<Color Us, GenType Type>
void generate_piece_moves(const Position& pos, MoveList& list, PieceType pt) {
    const U64 occupied = pos.pieces();
    const U64 targets = (Type == GEN_CAPTURES) ? pos.pieces(~Us) : ~pos.pieces(Us);

    U64 movers = pos.pieces(Us, pt);
    while (movers) {
        Square from = pop_lsb(movers);
        U64 attacks = attacks_of(pt, from, occupied) & targets;
        while (attacks) {
            Square to = pop_lsb(attacks);
            list.add(Move(from, to, pos.empty(to) ? QUIET : CAPTURE));
        }
    }
}

// ---------------------------------------------------------------------------
// Castling.  The rights flags only record that neither the king nor the rook
// has moved; the remaining conditions have to be checked here.  Note that the
// square the king passes over must be safe as well as its destination, which is
// why the transit square is tested explicitly.
// ---------------------------------------------------------------------------

template<Color Us>
void generate_castling(const Position& pos, MoveList& list) {
    constexpr Color Them = ~Us;
    constexpr Square KingFrom = (Us == WHITE) ? SQ_E1 : SQ_E8;
    constexpr int KingSide = (Us == WHITE) ? WHITE_OO : BLACK_OO;
    constexpr int QueenSide = (Us == WHITE) ? WHITE_OOO : BLACK_OOO;

    const int rights = pos.castling_rights();
    if (!(rights & (KingSide | QueenSide))) return;

    const U64 occupied = pos.pieces();

    // The king must not currently be in check, so this test is shared.
    if (pos.attacked_by(KingFrom, Them)) return;

    if (rights & KingSide) {
        constexpr Square F = (Us == WHITE) ? SQ_F1 : SQ_F8;
        constexpr Square G = (Us == WHITE) ? SQ_G1 : SQ_G8;
        if (!(occupied & (square_bb(F) | square_bb(G)))
            && !pos.attacked_by(F, Them)
            && !pos.attacked_by(G, Them))
            list.add(Move(KingFrom, G, CASTLE_OO));
    }

    if (rights & QueenSide) {
        constexpr Square B = (Us == WHITE) ? SQ_B1 : SQ_B8;
        constexpr Square C = (Us == WHITE) ? SQ_C1 : SQ_C8;
        constexpr Square D = (Us == WHITE) ? SQ_D1 : SQ_D8;
        // b1/b8 only has to be empty -- the king never stands on it, so it
        // does not matter whether it is attacked.
        if (!(occupied & (square_bb(B) | square_bb(C) | square_bb(D)))
            && !pos.attacked_by(D, Them)
            && !pos.attacked_by(C, Them))
            list.add(Move(KingFrom, C, CASTLE_OOO));
    }
}

// Every move of one colour, in one place: the pawns, then each piece type, then
// castling -- which capture generation skips, since castling never captures.
template<Color Us, GenType Type>
void generate_all(const Position& pos, MoveList& list) {
    generate_pawn_moves<Us, Type>(pos, list);
    generate_piece_moves<Us, Type>(pos, list, KNIGHT);
    generate_piece_moves<Us, Type>(pos, list, BISHOP);
    generate_piece_moves<Us, Type>(pos, list, ROOK);
    generate_piece_moves<Us, Type>(pos, list, QUEEN);
    generate_piece_moves<Us, Type>(pos, list, KING);
    if (Type != GEN_CAPTURES) generate_castling<Us>(pos, list);
}

} // namespace

// Picks the colour and generation type the templates above were compiled for.
// Choosing here, once per call, means neither the colour nor "captures only" is
// ever tested inside the loops that do the work.
void generate_pseudo_legal(const Position& pos, MoveList& list, GenType type) {
    if (pos.side_to_move() == WHITE) {
        type == GEN_CAPTURES ? generate_all<WHITE, GEN_CAPTURES>(pos, list)
                             : generate_all<WHITE, GEN_ALL>(pos, list);
    } else {
        type == GEN_CAPTURES ? generate_all<BLACK, GEN_CAPTURES>(pos, list)
                             : generate_all<BLACK, GEN_ALL>(pos, list);
    }
}

// Generates the pseudo-legal moves and keeps only those that leave our own king
// safe.  The search does not use this: it filters move by move as it goes, so
// that a cutoff saves it from checking the rest.
void generate_legal(const Position& pos, MoveList& list) {
    MoveList pseudo;
    generate_pseudo_legal(pos, pseudo, GEN_ALL);
    for (const ScoredMove& sm : pseudo)
        if (pos.is_legal(sm.move)) list.add(sm.move);
}
