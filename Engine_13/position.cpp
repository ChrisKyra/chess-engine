#include "position.h"
#include "movegen.h"

#include <algorithm>
#include <cctype>
#include <sstream>

namespace Zobrist {
U64 PSQ[PIECE_NB][SQUARE_NB];
U64 SIDE;
U64 CASTLING[16];
U64 EP_FILE[8];

// Fills the Zobrist tables with random 64-bit numbers: one per piece per
// square, one for "Black to move", one per castling-rights combination and one
// per en passant file.  A position's key is the xor of the numbers that apply
// to it, so making a move only has to xor out what left and xor in what
// arrived.  The generator is a fixed-seed xorshift, so the numbers are the same
// on every run and a hash key means the same thing between sessions.
void init() {
    U64 state = 0x246C0DFB2F1D4A9BULL;
    auto next = [&state]() {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return state * 2685821657736338717ULL;
    };
    for (int p = 0; p < PIECE_NB; ++p)
        for (int s = 0; s < SQUARE_NB; ++s)
            PSQ[p][s] = next();
    SIDE = next();
    for (int i = 0; i < 16; ++i) CASTLING[i] = next();
    for (int f = 0; f < 8; ++f) EP_FILE[f] = next();
}
} // namespace Zobrist

namespace {

// Castling rights survive a move only if neither the origin nor the
// destination square is a king or rook home square.  ANDing both masks handles
// the king moving, a rook moving, and a rook being captured in one step.
int CASTLE_MASK[SQUARE_NB];

struct CastleMaskInit {
    CastleMaskInit() {
        for (int s = 0; s < SQUARE_NB; ++s) CASTLE_MASK[s] = ANY_CASTLING;
        CASTLE_MASK[SQ_E1] &= ~(WHITE_OO | WHITE_OOO);
        CASTLE_MASK[SQ_H1] &= ~WHITE_OO;
        CASTLE_MASK[SQ_A1] &= ~WHITE_OOO;
        CASTLE_MASK[SQ_E8] &= ~(BLACK_OO | BLACK_OOO);
        CASTLE_MASK[SQ_H8] &= ~BLACK_OO;
        CASTLE_MASK[SQ_A8] &= ~BLACK_OOO;
    }
} castle_mask_init;

const char* PIECE_CHARS = "PNBRQKpnbrqk";

} // namespace

// ---------------------------------------------------------------- board edits

void Position::put_piece(Piece pc, Square s) {
    board[s] = pc;
    by_type[type_of(pc)] |= square_bb(s);
    by_color[color_of(pc)] |= square_bb(s);
    occupied |= square_bb(s);
}

void Position::remove_piece(Square s) {
    Piece pc = board[s];
    by_type[type_of(pc)] &= ~square_bb(s);
    by_color[color_of(pc)] &= ~square_bb(s);
    occupied &= ~square_bb(s);
    board[s] = NO_PIECE;
}

void Position::move_piece(Square from, Square to) {
    Piece pc = board[from];
    U64 delta = square_bb(from) | square_bb(to);
    by_type[type_of(pc)] ^= delta;
    by_color[color_of(pc)] ^= delta;
    occupied ^= delta;
    board[from] = NO_PIECE;
    board[to] = pc;
}

// ---------------------------------------------------------------- setup

// The normal starting array, set up through the FEN reader so there is only one
// piece of code that builds a position.
void Position::set_start_position() {
    set_from_fen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
}

// Returns false, leaving the position untouched, when the FEN can't be played
// from: the placement must describe exactly eight ranks of eight squares, each
// side needs exactly one king, no pawn may stand on the first or last rank, and
// the side that has just moved can't be in check.  Castling rights and the en
// passant square are kept only when the pieces they rely on are really there,
// so move generation never moves a rook or captures a pawn that doesn't exist.
bool Position::set_from_fen(const std::string& fen) {
    std::istringstream in(fen);
    std::string placement, turn, castling = "-", ep = "-";
    if (!(in >> placement >> turn)) return false;
    if (turn != "w" && turn != "b") return false;
    in >> castling >> ep;

    // ---- piece placement, checked before anything is changed ---------------
    Piece squares[SQUARE_NB];
    std::fill(std::begin(squares), std::end(squares), NO_PIECE);

    int rank = 7, file = 0;
    for (char c : placement) {
        if (c == '/') {
            if (file != 8 || rank == 0) return false;
            --rank;
            file = 0;
        } else if (c >= '1' && c <= '8') {
            file += c - '0';
            if (file > 8) return false;
        } else {
            const char* found = c ? std::strchr(PIECE_CHARS, c) : nullptr;
            if (!found || file > 7) return false;
            squares[make_square(file, rank)] = Piece(found - PIECE_CHARS);
            ++file;
        }
    }
    if (rank != 0 || file != 8) return false;

    Square king[COLOR_NB] = { NO_SQUARE, NO_SQUARE };
    U64 occ = 0;
    for (int s = 0; s < SQUARE_NB; ++s) {
        const Piece pc = squares[s];
        if (pc == NO_PIECE) continue;
        occ |= square_bb(Square(s));
        if (type_of(pc) == PAWN && (rank_of(Square(s)) == 0 || rank_of(Square(s)) == 7)) return false;
        if (type_of(pc) == KING) {
            if (king[color_of(pc)] != NO_SQUARE) return false;
            king[color_of(pc)] = Square(s);
        }
    }
    if (king[WHITE] == NO_SQUARE || king[BLACK] == NO_SQUARE) return false;

    // The side to move could capture the enemy king.
    const Color us = (turn == "w") ? WHITE : BLACK;
    for (int s = 0; s < SQUARE_NB; ++s) {
        const Piece pc = squares[s];
        if (pc == NO_PIECE || color_of(pc) != us) continue;
        const U64 attacks = type_of(pc) == PAWN ? pawn_attacks(us, Square(s))
                                                : attacks_of(type_of(pc), Square(s), occ);
        if (attacks & square_bb(king[~us])) return false;
    }

    // ---- valid: set the position up -----------------------------------------
    for (int i = 0; i < PIECE_TYPE_NB; ++i) by_type[i] = 0;
    by_color[WHITE] = by_color[BLACK] = occupied = 0;
    for (int s = 0; s < SQUARE_NB; ++s) board[s] = NO_PIECE;
    for (int s = 0; s < SQUARE_NB; ++s)
        if (squares[s] != NO_PIECE) put_piece(squares[s], Square(s));
    history.clear();
    history.emplace_back();

    side = us;

    StateInfo& st = history.back();
    st.castling_rights = NO_CASTLING;
    for (char c : castling) {
        if (c == 'K' && board[SQ_E1] == W_KING && board[SQ_H1] == W_ROOK) st.castling_rights |= WHITE_OO;
        else if (c == 'Q' && board[SQ_E1] == W_KING && board[SQ_A1] == W_ROOK) st.castling_rights |= WHITE_OOO;
        else if (c == 'k' && board[SQ_E8] == B_KING && board[SQ_H8] == B_ROOK) st.castling_rights |= BLACK_OO;
        else if (c == 'q' && board[SQ_E8] == B_KING && board[SQ_A8] == B_ROOK) st.castling_rights |= BLACK_OOO;
    }

    // The en passant square must sit behind an enemy pawn that could just have
    // made a double push: that pawn in place, and the two squares it crossed empty.
    // It is also dropped when no capture onto it is legal, exactly as make_move()
    // does, so a position has the same key however it was reached.
    st.ep_square = NO_SQUARE;
    if (ep.size() == 2 && ep[0] >= 'a' && ep[0] <= 'h' && ep[1] == (us == WHITE ? '6' : '3')) {
        const Square sq = make_square(ep[0] - 'a', ep[1] - '1');
        const Square pawn = (us == WHITE) ? Square(sq - 8) : Square(sq + 8);
        const Square origin = (us == WHITE) ? Square(sq + 8) : Square(sq - 8);
        if (board[sq] == NO_PIECE && board[origin] == NO_PIECE && board[pawn] == make_piece(~us, PAWN)
            && can_capture_en_passant(sq))
            st.ep_square = sq;
    }

    int halfmove = 0, fullmove_number = 1;
    if (!(in >> halfmove)) halfmove = 0;
    if (!(in >> fullmove_number)) fullmove_number = 1;
    st.halfmove_clock = std::max(halfmove, 0);
    fullmove = std::max(fullmove_number, 1);

    st.key = compute_key();
    return true;
}

// Writes the position back out as a FEN: the board from rank 8 down with empty
// squares run-length coded, then the side to move, castling rights, en passant
// square, halfmove clock and move number.  The inverse of set_from_fen().
std::string Position::fen() const {
    std::ostringstream out;
    for (int rank = 7; rank >= 0; --rank) {
        int gap = 0;
        for (int file = 0; file <= 7; ++file) {
            Piece pc = board[make_square(file, rank)];
            if (pc == NO_PIECE) { ++gap; continue; }
            if (gap) { out << gap; gap = 0; }
            out << PIECE_CHARS[pc];
        }
        if (gap) out << gap;
        if (rank) out << '/';
    }

    out << (side == WHITE ? " w " : " b ");

    int rights = state().castling_rights;
    if (!rights) out << '-';
    else {
        if (rights & WHITE_OO) out << 'K';
        if (rights & WHITE_OOO) out << 'Q';
        if (rights & BLACK_OO) out << 'k';
        if (rights & BLACK_OOO) out << 'q';
    }

    out << ' ' << square_name(state().ep_square)
        << ' ' << state().halfmove_clock << ' ' << fullmove;
    return out.str();
}

// Builds the Zobrist key of the current position from scratch, by xoring one
// random number per piece on its square plus the side, castling and en passant
// terms.  Only used when a position is set up; from then on make_move() keeps
// the key up to date incrementally, which is far cheaper.
U64 Position::compute_key() const {
    U64 k = 0;
    for (int s = 0; s < SQUARE_NB; ++s)
        if (board[s] != NO_PIECE) k ^= Zobrist::PSQ[board[s]][s];
    if (side == BLACK) k ^= Zobrist::SIDE;
    k ^= Zobrist::CASTLING[state().castling_rights];
    if (state().ep_square != NO_SQUARE) k ^= Zobrist::EP_FILE[file_of(state().ep_square)];
    return k;
}

// ---------------------------------------------------------------- attacks

// Every piece of either colour that attacks `s`, given an occupancy.
//
// The trick in the pawn lines is that "a white pawn attacks s" is the same as
// "a black pawn on s would attack that white pawn", so the precomputed attack
// table can be read backwards instead of shifting the pawn bitboards.  Used by
// static exchange evaluation, which needs the whole set at once.
U64 Position::attackers_to(Square s, U64 occ) const {
    return (pawn_attacks(WHITE, s) & pieces(BLACK, PAWN))
         | (pawn_attacks(BLACK, s) & pieces(WHITE, PAWN))
         | (knight_attacks(s) & by_type[KNIGHT])
         | (king_attacks(s) & by_type[KING])
         | (rook_attacks(s, occ) & (by_type[ROOK] | by_type[QUEEN]))
         | (bishop_attacks(s, occ) & (by_type[BISHOP] | by_type[QUEEN]));
}

// Is `s` attacked by any piece of colour `by`?  The same question as
// attackers_to(), but it stops at the first attacker found, which is all the
// legality test and castling need.  `enemies` exists so that a piece about to
// be captured can be left out even though its square is still occupied.
bool Position::attacked_by(Square s, Color by, U64 occ, U64 enemies) const {
    if (pawn_attacks(~by, s) & by_type[PAWN] & enemies) return true;
    if (knight_attacks(s) & by_type[KNIGHT] & enemies) return true;
    if (king_attacks(s) & by_type[KING] & enemies) return true;
    if (rook_attacks(s, occ) & (by_type[ROOK] | by_type[QUEEN]) & enemies) return true;
    if (bishop_attacks(s, occ) & (by_type[BISHOP] | by_type[QUEEN]) & enemies) return true;
    return false;
}

bool Position::attacked_by(Square s, Color by, U64 occ) const {
    return attacked_by(s, by, occ, by_color[by] & occ);
}

// ---------------------------------------------------------------- legality
//
// The generator produces pseudo-legal moves, so the only thing left to verify
// is that our own king is not left under attack.  Rather than making the move
// on the board, the resulting occupancy is assembled directly and the king
// square tested once.  This is O(1) and handles pins, discovered checks and
// the awkward en-passant case (where two pieces leave the same rank) uniformly.

bool Position::is_legal(Move m) const {
    Color us = side, them = ~us;
    Square from = m.from(), to = m.to();

    // Castling legality is fully established while generating the move: the
    // squares are empty and neither the origin, the transit square nor the
    // destination is attacked.
    if (m.is_castle()) return true;

    U64 occ = occupied & ~square_bb(from);
    U64 enemies = by_color[them];

    if (m.is_en_passant()) {
        Square captured = Square(int(to) - push_delta(us));
        occ &= ~square_bb(captured);
        enemies &= ~square_bb(captured);
    } else if (m.is_capture()) {
        enemies &= ~square_bb(to);
    }
    occ |= square_bb(to);

    Square ksq = (type_of(board[from]) == KING) ? to : king_square(us);
    return !attacked_by(ksq, them, occ, enemies);
}

// Whether the side to move can legally capture en passant onto `ep`.
//
// Engines 1 and 3-8 kept the square (and hashed it) after every double push,
// even when no pawn could take.  The same position then had two keys depending
// on how it was reached -- after 1.e4 e5 and after 1.e4 e5 2.Nf3 Nc6 3.Ng1 Nb8
// -- so transpositions were missed and, worse, so were repetitions: the engine
// walked into threefold repetitions it could not see, which no amount of
// contempt can prevent.  The fix from ../engine_2, carried forward.
bool Position::can_capture_en_passant(Square ep) const {
    if (relative_rank(side, ep) != 5 || !empty(ep)) return false;
    // The pawn that just double-pushed must be directly beyond the square.
    if (board[int(ep) - push_delta(side)] != make_piece(~side, PAWN)) return false;

    U64 capturers = pawn_attacks(~side, ep) & pieces(side, PAWN);
    while (capturers)
        if (is_legal(Move(pop_lsb(capturers), ep, EN_PASSANT)))
            return true;
    return false;
}

// ---------------------------------------------------------------- make/unmake

// Plays a move: moves the pieces, updates the Zobrist key move by move, and
// pushes a new state record so that unmake_move() can restore everything the
// move destroyed (the captured piece, the castling rights, the en passant
// square, the halfmove clock).
//
// The order matters: the old en passant square is xored out of the key before
// it is replaced, captures happen before the mover arrives on the square, and a
// promotion removes the pawn from the destination before putting the new piece
// there.  Castling rights are cleared through a per-square mask, which covers
// the king moving, a rook moving and a rook being captured in one step.
void Position::make_move(Move m) {
    StateInfo st = history.back();
    st.captured = NO_PIECE;
    U64 k = st.key;

    const Square from = m.from();
    const Square to = m.to();
    const Piece pc = board[from];
    const Color us = side;

    if (st.ep_square != NO_SQUARE) k ^= Zobrist::EP_FILE[file_of(st.ep_square)];
    st.ep_square = NO_SQUARE;
    Square ep_candidate = NO_SQUARE;
    ++st.halfmove_clock;
    ++st.plies_from_null;

    if (m.is_castle()) {
        bool kingside = (m.type() == CASTLE_OO);
        Square rook_from = kingside ? (us == WHITE ? SQ_H1 : SQ_H8)
                                    : (us == WHITE ? SQ_A1 : SQ_A8);
        Square rook_to = kingside ? (us == WHITE ? SQ_F1 : SQ_F8)
                                  : (us == WHITE ? SQ_D1 : SQ_D8);
        Piece rook = make_piece(us, ROOK);

        move_piece(from, to);
        k ^= Zobrist::PSQ[pc][from] ^ Zobrist::PSQ[pc][to];
        move_piece(rook_from, rook_to);
        k ^= Zobrist::PSQ[rook][rook_from] ^ Zobrist::PSQ[rook][rook_to];
    } else {
        if (m.is_en_passant()) {
            Square captured_sq = Square(int(to) - push_delta(us));
            st.captured = board[captured_sq];
            k ^= Zobrist::PSQ[st.captured][captured_sq];
            remove_piece(captured_sq);
            st.halfmove_clock = 0;
        } else if (m.is_capture()) {
            st.captured = board[to];
            k ^= Zobrist::PSQ[st.captured][to];
            remove_piece(to);
            st.halfmove_clock = 0;
        }

        move_piece(from, to);
        k ^= Zobrist::PSQ[pc][from] ^ Zobrist::PSQ[pc][to];

        if (type_of(pc) == PAWN) {
            st.halfmove_clock = 0;

            if (m.type() == DOUBLE_PUSH) {
                // Decided below, once the opponent is to move.
                ep_candidate = Square(int(from) + push_delta(us));
            } else if (m.is_promotion()) {
                Piece promoted = make_piece(us, m.promotion_type());
                k ^= Zobrist::PSQ[pc][to] ^ Zobrist::PSQ[promoted][to];
                remove_piece(to);
                put_piece(promoted, to);
            }
        }
    }

    int old_rights = st.castling_rights;
    st.castling_rights &= CASTLE_MASK[from] & CASTLE_MASK[to];
    if (old_rights != st.castling_rights)
        k ^= Zobrist::CASTLING[old_rights] ^ Zobrist::CASTLING[st.castling_rights];

    side = ~us;
    k ^= Zobrist::SIDE;
    if (us == BLACK) ++fullmove;

    // After a double push the square counts, and is hashed, only if the
    // opponent -- now to move -- can legally take en passant.
    if (ep_candidate != NO_SQUARE && can_capture_en_passant(ep_candidate)) {
        st.ep_square = ep_candidate;
        k ^= Zobrist::EP_FILE[file_of(ep_candidate)];
    }

    st.key = k;
    history.push_back(st);
}

// Takes the move back, in the reverse order of make_move().  Nothing is
// recomputed: the state record that make_move() pushed holds the captured piece
// and the key, and popping it restores the rest.
void Position::unmake_move(Move m) {
    const StateInfo st = history.back();
    history.pop_back();

    side = ~side;
    const Color us = side;
    if (us == BLACK) --fullmove;

    const Square from = m.from();
    const Square to = m.to();

    if (m.is_castle()) {
        bool kingside = (m.type() == CASTLE_OO);
        Square rook_from = kingside ? (us == WHITE ? SQ_H1 : SQ_H8)
                                    : (us == WHITE ? SQ_A1 : SQ_A8);
        Square rook_to = kingside ? (us == WHITE ? SQ_F1 : SQ_F8)
                                  : (us == WHITE ? SQ_D1 : SQ_D8);
        move_piece(to, from);
        move_piece(rook_to, rook_from);
        return;
    }

    if (m.is_promotion()) {
        remove_piece(to);
        put_piece(make_piece(us, PAWN), to);
    }

    move_piece(to, from);

    if (m.is_en_passant()) {
        Square captured_sq = Square(int(to) - push_delta(us));
        put_piece(st.captured, captured_sq);
    } else if (m.is_capture()) {
        put_piece(st.captured, to);
    }
}

// Passes the move to the opponent without moving anything, which is what null
// move pruning searches.  No piece moves, so only the side to move and the en
// passant square change.  plies_from_null is reset to zero: a position reached
// before a pass cannot really be repeated, and is_repetition() stops there.
void Position::make_null_move() {
    StateInfo st = history.back();
    st.captured = NO_PIECE;
    U64 k = st.key;

    if (st.ep_square != NO_SQUARE) k ^= Zobrist::EP_FILE[file_of(st.ep_square)];
    st.ep_square = NO_SQUARE;
    ++st.halfmove_clock;
    st.plies_from_null = 0;

    side = ~side;
    k ^= Zobrist::SIDE;
    st.key = k;
    history.push_back(st);
}

void Position::unmake_null_move() {
    history.pop_back();
    side = ~side;
}

// ---------------------------------------------------------------- draws

// Has this position occurred before?  Only positions since the last capture or
// pawn move can match (nothing else can be undone), and only every second
// record has the same side to move, hence the step of two.
bool Position::is_repetition(int search_ply) const {
    const U64 k = state().key;
    // A position before the last null move can't be repeated for real: getting
    // back to it took a "pass", which isn't a legal move.
    const int limit = std::min({ state().halfmove_clock, state().plies_from_null, int(history.size()) - 1 });
    int seen = 0;

    // Only positions with the same side to move can repeat, hence the step of 2.
    for (int back = 4; back <= limit; back += 2) {
        const StateInfo& prev = history[history.size() - 1 - back];
        if (prev.key != k) continue;
        // Inside the current search tree a single repeat already means the
        // opponent can force the draw, so it is scored as one immediately.
        if (back <= search_ply) return true;
        if (++seen == 2) return true;
    }
    return false;
}

// True for the material combinations in which no sequence of legal moves can
// ever deliver mate: bare kings, king and one minor piece, and two bishops that
// travel on the same colour squares.  Pawns, rooks or queens always leave mate
// possible, so their presence answers the question immediately.
bool Position::is_insufficient_material() const {
    if (by_type[PAWN] | by_type[ROOK] | by_type[QUEEN]) return false;

    U64 minors = by_type[KNIGHT] | by_type[BISHOP];
    int count = popcount(minors);
    if (count <= 1) return true;                       // bare kings, or K+minor
    if (count == 2 && popcount(by_type[BISHOP]) == 2) {
        // Two bishops only draw when they run on same-coloured squares.
        U64 dark = 0xAA55AA55AA55AA55ULL;
        U64 bishops = by_type[BISHOP];
        return popcount(bishops & dark) != 1;
    }
    return false;
}

// ---------------------------------------------------------------- exchanges

namespace {

// Values for exchange evaluation only.  The king's is huge because a king can
// never be traded; it only recaptures when that is safe (see below).
constexpr int SEE_VALUE[PIECE_TYPE_NB] = { 100, 320, 330, 500, 975, 20000 };

} // namespace

// The "swap" algorithm: gain[d] is what the side making the d-th capture has
// won if the exchange stopped there.  Walking back from the end, each side
// takes the better of capturing or stopping, which yields the value of the
// whole sequence.  Sliders hidden behind the capturing pieces join in as the
// occupancy is updated (x-rays).
int Position::see(Move m) const {
    if (m.is_castle()) return 0;

    const Square from = m.from(), to = m.to();
    int gain[32];
    int d = 0;
    Color mover = side;
    PieceType on_target = type_of(board[from]);   // the piece now standing on `to`
    U64 occ = occupied ^ square_bb(from);

    if (m.is_en_passant()) {
        gain[0] = SEE_VALUE[PAWN];
        occ ^= square_bb(Square(int(to) - push_delta(side)));
    } else {
        gain[0] = empty(to) ? 0 : SEE_VALUE[type_of(board[to])];
    }
    if (m.is_promotion()) {
        gain[0] += SEE_VALUE[m.promotion_type()] - SEE_VALUE[PAWN];
        on_target = m.promotion_type();
    }

    while (d < 31) {
        mover = ~mover;
        const U64 attackers = attackers_to(to, occ) & occ;
        const U64 ours = attackers & by_color[mover];
        if (!ours) break;

        // The least valuable attacker takes back first.
        int pt = PAWN;
        while (!(ours & by_type[pt])) ++pt;

        // A king may only recapture when the other side has nothing left to
        // take it with.
        if (pt == KING && (attackers & by_color[~mover])) break;

        ++d;
        gain[d] = SEE_VALUE[on_target] - gain[d - 1];
        occ ^= square_bb(lsb(ours & by_type[pt]));
        on_target = PieceType(pt);
    }

    while (d > 0) {
        gain[d - 1] = -std::max(-gain[d - 1], gain[d]);
        --d;
    }
    return gain[0];
}

// ---------------------------------------------------------------- helpers

// Turns "e2e4" or "e7e8q" into the move the generator would produce for it, by
// generating the legal moves and finding the one with those squares and that
// promotion piece.  Going through the generator is what fills in the move type
// (capture, double push, castling, en passant), which the text does not say.
// Returns MOVE_NONE when the text names no legal move.
Move Position::parse_uci_move(const std::string& text) const {
    if (text.size() < 4) return MOVE_NONE;

    Square from = make_square(text[0] - 'a', text[1] - '1');
    Square to = make_square(text[2] - 'a', text[3] - '1');
    PieceType promo = NO_PIECE_TYPE;
    if (text.size() >= 5) {
        switch (std::tolower(static_cast<unsigned char>(text[4]))) {
            case 'n': promo = KNIGHT; break;
            case 'b': promo = BISHOP; break;
            case 'r': promo = ROOK; break;
            case 'q': promo = QUEEN; break;
            default: break;
        }
    }

    MoveList list;
    generate_legal(*this, list);
    for (const ScoredMove& sm : list) {
        Move m = sm.move;
        if (m.from() != from || m.to() != to) continue;
        if (m.is_promotion()) {
            if (m.promotion_type() == promo) return m;
        } else if (promo == NO_PIECE_TYPE) {
            return m;
        }
    }
    return MOVE_NONE;
}

// The board drawn as text, with the FEN and the hash key underneath: what the
// "d" command prints when you want to see what the engine thinks it is playing.
std::string Position::to_string() const {
    std::ostringstream out;
    out << "\n +---+---+---+---+---+---+---+---+\n";
    for (int rank = 7; rank >= 0; --rank) {
        for (int file = 0; file <= 7; ++file) {
            Piece pc = board[make_square(file, rank)];
            out << " | " << (pc == NO_PIECE ? ' ' : PIECE_CHARS[pc]);
        }
        out << " | " << (rank + 1) << "\n +---+---+---+---+---+---+---+---+\n";
    }
    out << "   a   b   c   d   e   f   g   h\n\n"
        << "FEN: " << fen() << "\nKey: " << std::hex << key() << std::dec << "\n";
    return out.str();
}
