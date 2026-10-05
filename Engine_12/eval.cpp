#include "eval.h"
#include "bitbase.h"
#include "movegen.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <iterator>
#include <string>
#include <vector>

namespace Eval {

const int PIECE_VALUE[PIECE_TYPE_NB] = { 100, 320, 335, 500, 975, 0 };

namespace {

// ---------------------------------------------------------------------------
// Material.  Two sets of values are kept: one for the middlegame and one for
// the endgame.  Rooks and pawns gain value as the board empties, minor pieces
// roughly hold theirs, so a single number cannot describe both phases.
// ---------------------------------------------------------------------------

const int MG_MATERIAL[PIECE_TYPE_NB] = { 81, 384, 384, 588, 1121, 0 };
const int EG_MATERIAL[PIECE_TYPE_NB] = { 114, 339, 384, 615, 1216, 0 };

// ---------------------------------------------------------------------------
// Piece-square tables, written as they appear on the board with rank 8 on the
// first line, from White's point of view.  Index 0 is therefore a8, which is
// why the lookup below flips the square for White and uses it directly for
// Black (a black piece on a3 is the mirror of a white piece on a6).
// ---------------------------------------------------------------------------

const int MG_PAWN[64] = {
       0,   0,   0,   0,   0,   0,   0,   0,
      83,  24,  34,  69,   7,  34,  38,  30,
      26, -65,  53,  -2,  29,  25, -44,  28,
     -16, -27, -15,  -2, -12,  12, -25, -13,
     -13, -21,  -9,   1,  -5, -23, -26,  -9,
     -17,   1, -17,  -8, -16, -15,  14,  -4,
     -32,  -5,  -9, -70, -13,   7,  21,  -1,
       0,   0,   0,   0,   0,   0,   0,   0
};

const int EG_PAWN[64] = {
       0,   0,   0,   0,   0,   0,   0,   0,
      57,  52, 101,  43,  61,  89, 123,  -7,
       3,   5,  -7,  24,  14, -29,   5, -47,
       3, -22, -25, -41, -16, -24,   1, -28,
     -10, -25, -19, -28, -13,  -9,   3, -19,
      -2, -12, -10, -19, -11, -10, -20, -22,
      -4, -20, -14,  -8, -22,   2, -20, -23,
       0,   0,   0,   0,   0,   0,   0,   0
};

const int MG_KNIGHT[64] = {
    -130, -40, -24, -29, -29, -24, -40,-130,
     -26,   9,  15,  31,  31,  15,   9, -26,
     -15,  68,  25,  61,  61,  25,  68, -15,
      37,  35,  37,  47,  47,  37,  35,  37,
      -6,  16,  28,  38,  38,  28,  16,  -6,
      -9,   2,   7,   1,   1,   7,   2,  -9,
     -26, -26,   1,  16,  16,   1, -26, -26,
     -72, -13, -43, -15, -15, -43, -13, -72
};

const int EG_KNIGHT[64] = {
    -100, -31,  -5,  13,  13,  -5, -31,-100,
      -5,  21, -13,  23,  23, -13,  21,  -5,
      -7,  24,   9,  11,  11,   9,  24,  -7,
      -7,  12,  29,  25,  25,  29,  12,  -7,
      15,  18,  32,   9,   9,  32,  18,  15,
       2, -17,  35,  18,  18,  35, -17,   2,
     -15,  17,  -5,   6,   6,  -5,  17, -15,
     -78,   4, -29, -12, -12, -29,   4, -78
};

const int MG_BISHOP[64] = {
     -36, -12,  -4,  12,  12,  -4, -12, -36,
     -37, -11, -12,  33,  33, -12, -11, -37,
     -11, -18,   8,  14,  14,   8, -18, -11,
     -24,   3,  22,  24,  24,  22,   3, -24,
      20, -24,   9,  49,  49,   9, -24,  20,
       8,  22, -11,  13,  13, -11,  22,   8,
     -70,  19,  28,   0,   0,  28,  19, -70,
      18, -19,  10, -26, -26,  10, -19,  18
};

const int EG_BISHOP[64] = {
     -14,  10,  -8,  26,  26,  -8,  10, -14,
      37, -15,  20,  12,  12,  20, -15,  37,
       9,  25,   7,  20,  20,   7,  25,   9,
      -1,  10,   0,  10,  10,   0,  10,  -1,
       5,  18,  32,  -8,  -8,  32,  18,   5,
      -7,  -5, -10,   5,   5, -10,  -5,  -7,
     -40, -43, -20,   7,   7, -20, -43, -40,
     -24, -40, -12,  -5,  -5, -12, -40, -24
};

const int MG_ROOK[64] = {
      18,   9,  -5,   9,   9,  -5,   9,  18,
      14, -38, -11,  32,  32, -11, -38,  14,
     -53,  -4,   0,  22,  22,   0,  -4, -53,
       0,  45,  -4,  24,  24,  -4,  45,   0,
     -19,  14,  -2,   7,   7,  -2,  14, -19,
     -26, -35,   0, -31, -31,   0, -35, -26,
      -5,   5,   5,  20,  20,   5,   5,  -5,
      -4,  -8,  12,   9,   9,  12,  -8,  -4
};

const int EG_ROOK[64] = {
      15,  15,  11,  10,  10,  11,  15,  15,
       6,  12,  19,   3,   3,  19,  12,   6,
      44,  17,  23,  21,  21,  23,  17,  44,
      15,   7,  28,  22,  22,  28,   7,  15,
      -6,  13,   0,   5,   5,   0,  13,  -6,
     -19, -10, -11,   2,   2, -11, -10, -19,
     -26, -25, -35, -43, -43, -35, -25, -26,
     -33, -22, -22, -35, -35, -22, -22, -33
};

const int MG_QUEEN[64] = {
      -5,  11,  11,  28,  28,  11,  11,  -5,
     -16, -13,  15,   5,   5,  15, -13, -16,
      21,  33,  30,  26,  26,  30,  33,  21,
       1,   8,  27,   3,   3,  27,   8,   1,
       5, -23,  19,   6,   6,  19, -23,   5,
      15,  -4,   6,   3,   3,   6,  -4,  15,
     -46, -12, -19, -11, -11, -19, -12, -46,
     -33, -37, -44,  -8,  -8, -44, -37, -33
};

const int EG_QUEEN[64] = {
      -4,  16,  10,  27,  27,  10,  16,  -4,
      20,  28,  31,  55,  55,  31,  28,  20,
      -5,  -5,  10,  46,  46,  10,  -5,  -5,
      -7,  17,  21,  52,  52,  21,  17,  -7,
     -49,  14,  25,  19,  19,  25,  14, -49,
     -43, -45,   7, -26, -26,   7, -45, -43,
     -29,  -6, -25,  -7,  -7, -25,  -6, -29,
     -38, -35, -45, -31, -31, -45, -35, -38
};

// In the middlegame the king belongs tucked behind its pawns; in the endgame it
// becomes a fighting piece and wants the centre.  The two tables disagree
// sharply, which is exactly what the phase interpolation is for.
const int MG_KING[64] = {
     -37, -49, -47, -51, -65, -46, -47, -46,
     -41, -38, -49, -60, -57, -50, -37, -47,
     -44, -44, -54, -66, -67, -66, -56, -40,
     -32, -28, -48, -41, -68, -63, -49, -51,
     -26,  -1, -12, -52, -24, -52, -38, -34,
      -8, -10, -32,  -1, -56, -32,  -7, -19,
      72,  26,  -3, -41, -35, -18, -11,   8,
       7,  15,  15, -64, -23, -53,   8,   9
};

const int EG_KING[64] = {
     -45, -29, -18,  15, -61, -12, -23, -80,
     -17,  28,  28,  28,  36,  38,  41, -28,
       4,  37,  27,  37,  41,  21,  47,   1,
      18,  20,  27,  29,  15,  34,  14, -27,
     -24,   9,  12,   7,   8,  13,  -1, -15,
      -1,  -4,  -1,  -9,   7,   4,   8, -15,
     -44, -12,  -1,  -5,  -7, -13,   6, -25,
     -72, -25, -30,  -4,   0, -15, -21, -39
};

const int* MG_TABLE[PIECE_TYPE_NB] = { MG_PAWN, MG_KNIGHT, MG_BISHOP, MG_ROOK, MG_QUEEN, MG_KING };
const int* EG_TABLE[PIECE_TYPE_NB] = { EG_PAWN, EG_KNIGHT, EG_BISHOP, EG_ROOK, EG_QUEEN, EG_KING };

// Combined material + square score, indexed by [piece][square], built once.
int MG_PSQ[PIECE_NB][SQUARE_NB];
int EG_PSQ[PIECE_NB][SQUARE_NB];

// ---------------------------------------------------------------------------
// Masks and tables filled in by init().  All of them are "from the point of
// view of a colour", so the same code serves both sides.
// ---------------------------------------------------------------------------

U64 FILE_NEIGHBOURS[8];                 // the files either side of a given file
U64 FORWARD_FILE[COLOR_NB][SQUARE_NB];  // the rest of the file, ahead of a square
U64 PASSED_MASK[COLOR_NB][SQUARE_NB];   // the squares an enemy pawn must hold to stop a passer
U64 ATTACK_SPAN[COLOR_NB][SQUARE_NB];   // neighbouring files, ahead of a square
U64 REAR_SPAN[COLOR_NB][SQUARE_NB];     // neighbouring files, level with or behind a square
U64 OUTPOST_ZONE[COLOR_NB];             // the ranks an outpost is worth having
int DISTANCE[SQUARE_NB][SQUARE_NB];     // king moves between two squares

// The squares a bishop of each colour travels on, for the opposite-bishops test.
constexpr U64 DARK_SQUARES = 0xAA55AA55AA55AA55ULL;

// d4, e4, d5, e5.
constexpr U64 CENTER = (FILE_D_BB | FILE_E_BB) & (RANK_4_BB | RANK_5_BB);

// Our own pawns this far back are not worth counting as mobility blockers.
constexpr U64 LOW_RANKS[COLOR_NB] = { RANK_2_BB | RANK_3_BB, RANK_7_BB | RANK_6_BB };

// Index into attacked_by[] holding "every piece of this colour".
constexpr int ALL_PIECES = PIECE_TYPE_NB;

// ---------------------------------------------------------------------------
// Weights.  All hand-picked, not tuned.  Everything is a (middlegame, endgame)
// pair; the two are blended at the end according to how much material is left.
// ---------------------------------------------------------------------------

// Game phase weights.  Summed over all remaining pieces they give 24 at the
// start and 0 in a bare king endgame.
const int PHASE_WEIGHT[PIECE_TYPE_NB] = { 0, 1, 1, 2, 4, 0 };
constexpr int MAX_PHASE = 24;

constexpr int BISHOP_PAIR_MG = 49;
constexpr int BISHOP_PAIR_EG = 47;
constexpr int TEMPO = 12;

// ---- pawn structure ----
constexpr int DOUBLED_PAWN_MG = -4;
constexpr int DOUBLED_PAWN_EG = 5;
constexpr int ISOLATED_PAWN_MG = -3;
constexpr int ISOLATED_PAWN_EG = -22;
constexpr int BACKWARD_PAWN_MG = -12;
constexpr int BACKWARD_PAWN_EG = -20;

// Pawns defending each other or standing side by side, by relative rank.
const int CONNECTED_MG[8] = { 0, 3, 8, 7, 13, 44, 87, 0 };
const int CONNECTED_EG[8] = { 0, -2, -2, 3, 14, 35, 47, 0 };
constexpr int SUPPORTED_EG = 7;             // per pawn defending this one

// ---- passed pawns ----
const int PASSED_PAWN_MG[8] = { 0, 45, -18, 13, 39, 80, 89, 0 };
const int PASSED_PAWN_EG[8] = { 0, 22, 22, 23, 52, 49, 75, 0 };
const int PASSED_FREE_EG[8] = { 0, 14, 4, 9, 11, 59, 12, 0 };     // no piece in the way
const int PASSED_SAFE_EG[8] = { 0, -5, 13, 22, 46, 47, 129, 0 };      // and no enemy attack on the way
const int PASSED_BLOCKED_EG[8] = { 0, -24, -37, -12, -11, 6, -57, 0 };  // an enemy piece sits in front
const int PASSED_KING_WEIGHT[8] = { 0, 0, 0, 17, 27, 39, 35, 0 };   // per square of king distance
constexpr int ROOK_BEHIND_PASSER_EG = 55;
constexpr int PASSED_UNSTOPPABLE_EG = 402;  // the enemy king cannot catch it at all

// ---- pieces ----
// Mobility is counted in "safe squares this piece can reach", minus a baseline
// so that an average piece scores zero and only unusually free or unusually
// cramped pieces move the evaluation.
const int MOBILITY_MG[PIECE_TYPE_NB] = { 0, 10, 9, 5, 3, 0 };
const int MOBILITY_EG[PIECE_TYPE_NB] = { 0, -4, 1, 3, -5, 0 };
const int MOBILITY_BASE[PIECE_TYPE_NB] = { 0, 4, 6, 7, 13, 0 };

// ---- terms added in Engine 11 (Texel-tuned like everything else) ----
// A knight or bishop with one of our pawns right in front of it: shielded from
// frontal attack, and the pawn is defended along the file of approach.
constexpr int MINOR_BEHIND_PAWN_MG = 9;
constexpr int MINOR_BEHIND_PAWN_EG = 1;
// Per square of king distance, for a knight or bishop: pieces far from their own
// king leave it to defend itself.
const int KING_PROTECTOR_MG[PIECE_TYPE_NB] = { 0, -4, -2, 0, 0, 0 };
const int KING_PROTECTOR_EG[PIECE_TYPE_NB] = { 0, -3, 2, 0, 0, 0 };
// Per own pawn standing on the bishop's square colour: the pawns block it.
constexpr int BISHOP_PAWNS_MG = 3;
constexpr int BISHOP_PAWNS_EG = -11;
// A bishop that sees two of the four centre squares (through any pieces but
// pawns): the fianchettoed bishop on its long diagonal.
constexpr int LONG_DIAGONAL_BISHOP_MG = 28;
constexpr int LONG_DIAGONAL_BISHOP_EG = 12;
// A rook with at most three moves, shut in on its wing by its own king.
constexpr int TRAPPED_ROOK_MG = -17;
constexpr int TRAPPED_ROOK_EG = -41;


constexpr int ROOK_OPEN_FILE_MG = 36;
constexpr int ROOK_OPEN_FILE_EG = -6;
constexpr int ROOK_SEMI_OPEN_MG = 19;
constexpr int ROOK_SEMI_OPEN_EG = 0;
constexpr int ROOK_ON_SEVENTH_MG = 27;
constexpr int ROOK_ON_SEVENTH_EG = 29;
constexpr int KNIGHT_OUTPOST_MG = 33;
constexpr int KNIGHT_OUTPOST_EG = 21;
constexpr int BISHOP_OUTPOST_MG = 49;
constexpr int BISHOP_OUTPOST_EG = 13;

// ---- threats, by the type of piece being threatened ----
const int THREAT_BY_PAWN_MG[PIECE_TYPE_NB] = { 0, 47, 45, 29, 52, 0 };
const int THREAT_BY_PAWN_EG[PIECE_TYPE_NB] = { 0, 25, 48, 39, 57, 0 };
const int THREAT_BY_MINOR_MG[PIECE_TYPE_NB] = { 0, 0, 0, 65, 43, 0 };
const int THREAT_BY_MINOR_EG[PIECE_TYPE_NB] = { 0, 0, 0, 4, 31, 0 };
constexpr int THREAT_BY_ROOK_MG = 40;       // a rook attacking the queen
constexpr int THREAT_BY_ROOK_EG = 21;
constexpr int HANGING_MG = 7;              // per piece attacked and undefended
constexpr int HANGING_EG = 20;

// ---- king safety, middlegame only (the phase blend fades it out) ----
//
// Attack: every piece hitting the squares around the king adds "attack units" --
// its weight per square hit.  Danger grows with the square of the units, because
// a combined attack is worth far more than separate threats, and counts in full
// only when several pieces take part.
const int KING_ATTACK_WEIGHT[PIECE_TYPE_NB] = { 0, 2, 2, 3, 5, 0 };
const int KING_ATTACKER_SCALE[8] = { 0, 0, 50, 75, 88, 94, 97, 100 };   // percent, by attacker count
constexpr int KING_DANGER_MAX = 500;
constexpr int KING_ZONE_WEAK_SQUARE = 3;    // units per square we fail to defend twice

// Shelter: judged on the king's file and the files beside it.
constexpr int SHIELD_PAWN_ADVANCED = 3;    // the shield pawn has moved two squares up
constexpr int SHIELD_PAWN_MISSING = -3;    // no shield pawn within two squares
constexpr int KING_FILE_SEMI_OPEN = -15;    // none of our pawns on the file
constexpr int KING_FILE_OPEN = -43;         // no pawns of either colour on the file
// Squares from which an enemy piece of this type could give check without being
// taken (we do not attack them and they are not the enemy's own), per square.
const int SAFE_CHECK_MG[PIECE_TYPE_NB] = { 0, -38, -15, -29, -32, 0 };
const int SAFE_CHECK_EG[PIECE_TYPE_NB] = { 0, 5, -13, -8, 9, 0 };
// The enemy pawn closest to our king on its file or a neighbouring one, by its
// rank from our side (2 = right in front of a king on the back rank): pawns
// storming the king open lines towards it.  Middlegame only.
const int PAWN_STORM[8] = { 0, 22, -59, -4, -2, 3, 3, 0 };

// ---- endgame scaling ----
// The endgame score is multiplied by scale/SCALE_NORMAL before blending, which
// is how "an extra pawn here is worthless" gets expressed without touching the
// terms that produced the score.
constexpr int SCALE_NORMAL = 64;

// Whether to fade the score as the fifty-move clock runs down.  Only changed
// between searches.
bool fifty_move_scaling = true;
constexpr int SCALE_OPPOSITE_BISHOPS = 22;      // bishops of opposite colours, nothing else
constexpr int SCALE_OPPOSITE_BISHOPS_PIECES = 44;
constexpr int SCALE_NO_PAWNS_TINY_EDGE = 8;     // no pawns, less than a bishop ahead
constexpr int SCALE_NO_PAWNS_SMALL_EDGE = 24;   // no pawns, less than a rook ahead
constexpr int SCALE_BLOCKED_ROOK_PAWN = 16;     // rook and pawn against rook, defending king in front
constexpr int SCALE_BLOCKED_BISHOP_PAWN = 4;    // bishop and pawn against bishop, king blocking on the other colour

// ---------------------------------------------------------------------------
// Endgame knowledge.  A few endings are not evaluated term by term but
// recognised: their result is known.
// ---------------------------------------------------------------------------

// A score for a position that is won with correct play but not yet a mate the
// search can see: far above any material count, far below the mate scores.
constexpr int KNOWN_WIN = 10000;

// Driving a lone king to the edge: 20 in the centre, rising steeply towards the
// edges, 100 in a corner ...
int push_to_edge(Square s) {
    const int f = std::min(file_of(s), 7 - file_of(s));   // 0 on the edge, 3 in the centre
    const int r = std::min(rank_of(s), 7 - rank_of(s));
    const int centre = std::min(f, r) * 2 + std::max(f, r);   // 0 in a corner, 9 in the centre
    return 100 - centre * 80 / 9;
}

// ... and bringing our own king close, which is what the mating patterns need:
// worth most with one square between the kings.
int push_close(int distance) {
    constexpr int CLOSE[8] = { 0, 0, 100, 80, 60, 40, 20, 10 };
    return CLOSE[distance];
}

// The step (file plus rank) distance between two squares: unlike the king
// distance, it falls with every step along an edge towards a corner.
int steps(Square a, Square b) {
    return std::abs(file_of(a) - file_of(b)) + std::abs(rank_of(a) - rank_of(b));
}

// ---------------------------------------------------------------------------
// A middlegame and an endgame score accumulated in parallel.
// ---------------------------------------------------------------------------

struct Score {
    int mg = 0;
    int eg = 0;

    // Add a (middlegame, endgame) pair to this score.
    void add(int m, int e) { mg += m; eg += e; }

    // Subtract a pair, used for Black's material during the board sweep.
    void sub(int m, int e) { mg -= m; eg -= e; }

    // Fold another score into this one.
    Score& operator+=(const Score& other) { mg += other.mg; eg += other.eg; return *this; }
};

// ---------------------------------------------------------------------------
// Tuning trace.
//
// For Texel tuning (see tune.cpp) the evaluation has to say not only what it
// scores but how often each weight was used: then the score is a sum of
// coefficient x weight, and the weights can be fitted to game results.  Every
// tunable weight has an index below; TRACE(index, colour, count) records that
// the weight counted `count` times for that colour (White positive, Black
// negative).  Only the `tuner` build (EVAL_TRACE) records anything -- in the
// engine the macro is empty and costs nothing.
// ---------------------------------------------------------------------------

enum TraceIndex : int {
    T_MATERIAL = 0,                              // by piece type
    T_PSQT = T_MATERIAL + PIECE_TYPE_NB,         // piece type * 64 + table index (a8 = 0)
    T_BISHOP_PAIR = T_PSQT + PIECE_TYPE_NB * 64,
    T_DOUBLED, T_ISOLATED, T_BACKWARD,
    T_CONNECTED,                                 // by relative rank
    T_SUPPORTED = T_CONNECTED + 8,
    T_PASSED,                                    // by relative rank
    T_PASSED_FREE = T_PASSED + 8,
    T_PASSED_SAFE = T_PASSED_FREE + 8,
    T_PASSED_BLOCKED = T_PASSED_SAFE + 8,
    T_PASSED_KING = T_PASSED_BLOCKED + 8,
    T_ROOK_BEHIND_PASSER = T_PASSED_KING + 8,
    T_PASSED_UNSTOPPABLE,
    T_MOBILITY,                                  // by piece type
    T_ROOK_OPEN = T_MOBILITY + PIECE_TYPE_NB,
    T_ROOK_SEMI_OPEN, T_ROOK_SEVENTH, T_KNIGHT_OUTPOST, T_BISHOP_OUTPOST,
    T_THREAT_BY_PAWN,                            // by victim type
    T_THREAT_BY_MINOR = T_THREAT_BY_PAWN + PIECE_TYPE_NB,
    T_THREAT_BY_ROOK = T_THREAT_BY_MINOR + PIECE_TYPE_NB,
    T_HANGING,
    T_SHIELD_ADVANCED, T_SHIELD_MISSING, T_KING_FILE_SEMI_OPEN, T_KING_FILE_OPEN,
    T_MINOR_BEHIND_PAWN,
    T_KING_PROTECTOR,                            // by piece type
    T_BISHOP_PAWNS = T_KING_PROTECTOR + PIECE_TYPE_NB,
    T_LONG_DIAGONAL, T_TRAPPED_ROOK,
    T_SAFE_CHECK,                                // by checking piece type
    T_PAWN_STORM = T_SAFE_CHECK + PIECE_TYPE_NB, // by rank
    T_COUNT = T_PAWN_STORM + 8
};

#ifdef EVAL_TRACE
thread_local double trace_coefficients[T_COUNT];
// The totals of the last evaluation, before the phases are blended.
thread_local struct { int mg, eg, phase, scale; } trace_totals;
#define TRACE(index, color, count) \
    (trace_coefficients[(index)] += ((color) == WHITE ? 1.0 : -1.0) * double(count))
#else
#define TRACE(index, color, count) ((void)0)
#endif

// ---------------------------------------------------------------------------
// Pawn hash table.
//
// Pawn structure is expensive to work out and changes only when a pawn moves or
// is captured, which is rare compared with how often evaluate() is called.  The
// results are therefore cached, keyed on the two pawn bitboards.
//
// An entry stores both bitboards and is only used when they match exactly, so a
// hash collision costs a recomputation and can never give a wrong answer.  Only
// terms that depend on nothing but the pawns are cached here; anything that
// looks at pieces or kings (passed-pawn support, king distance) is recomputed
// from the cached passed-pawn bitboard every time.
// ---------------------------------------------------------------------------

struct PawnEntry {
    U64 pawns[COLOR_NB];      // the placement this entry describes
    U64 passed[COLOR_NB];     // pawns with no enemy pawn able to stop them
    U64 attacks[COLOR_NB];    // squares attacked by pawns of this colour
    U64 attacks2[COLOR_NB];   // squares attacked by two pawns of this colour
    Score score[COLOR_NB];    // the structure terms for this colour
};

// One table per searching thread.  The threads would otherwise be writing the
// same entries at the same time, and unlike the transposition table this one is
// too big to update in single machine words.  Half a megabyte per thread is a
// small price beside the table they do share.
//
// An all-zero entry is not a special "empty" marker -- it is the correct answer
// for a position with no pawns at all: no passers, no attacks, no structure
// score.  So a freshly zeroed table needs no initialisation pass.
constexpr size_t PAWN_TABLE_SIZE = 1 << 13;     // 8192 entries, about 0.5 MB
thread_local PawnEntry pawn_table[PAWN_TABLE_SIZE];

// Works out every structural term for one colour and records them in `e`:
// doubled, isolated, backward and connected pawns, and which pawns are passed.
//
// * doubled   -- a friendly pawn stands somewhere ahead on the same file, so the
//                two cannot defend each other and one of them can be blockaded.
// * isolated  -- no friendly pawn on either neighbouring file: nothing can ever
//                defend it, and the file in front of it is a hole.
// * backward  -- it has no friendly pawn beside or behind it on the neighbouring
//                files, and the square in front is covered by an enemy pawn, so
//                it cannot advance and cannot be defended by a pawn.
// * connected -- another pawn defends it or stands beside it; such pawns defend
//                each other as they advance, and the bonus grows with the rank.
// * passed    -- no enemy pawn on its file or the neighbouring files ahead of
//                it, so only pieces can stop it from queening.
template<Color Us>
void compute_pawn_structure(const Position& pos, PawnEntry& e) {
    constexpr Color Them = ~Us;
    const U64 ours = pos.pieces(Us, PAWN);
    const U64 theirs = pos.pieces(Them, PAWN);

    // Both pawn attack sets at once: one shift per diagonal, whole bitboard.
    const U64 left = pawn_attacks_left<Us>(ours);
    const U64 right = pawn_attacks_right<Us>(ours);
    e.attacks[Us] = left | right;
    e.attacks2[Us] = left & right;
    e.passed[Us] = 0;

    Score score;
    U64 pawns = ours;
    while (pawns) {
        const Square s = pop_lsb(pawns);
        const int f = file_of(s);
        const int r = relative_rank(Us, s);
        const Square stop = Square(int(s) + push_delta(Us));

        const U64 neighbours = FILE_NEIGHBOURS[f] & ours;
        const U64 support = pawn_attacks(Them, s) & ours;   // our pawns defending this one
        const U64 phalanx = (east(square_bb(s)) | west(square_bb(s))) & ours;
        const bool doubled = FORWARD_FILE[Us][s] & ours;
        const bool passed = !(PASSED_MASK[Us][s] & theirs);

        // A square is covered by an enemy pawn exactly when one of our pawns
        // placed there would attack an enemy pawn -- the same relation read
        // backwards, which saves computing their attack set here.
        const bool stop_covered = pawn_attacks(Us, stop) & theirs;
        const bool backward = !passed && !(REAR_SPAN[Us][s] & ours) && stop_covered;

        if (doubled) { score.add(DOUBLED_PAWN_MG, DOUBLED_PAWN_EG); TRACE(T_DOUBLED, Us, 1); }

        if (!neighbours) { score.add(ISOLATED_PAWN_MG, ISOLATED_PAWN_EG); TRACE(T_ISOLATED, Us, 1); }
        else if (backward) { score.add(BACKWARD_PAWN_MG, BACKWARD_PAWN_EG); TRACE(T_BACKWARD, Us, 1); }

        if (support || phalanx) {
            int mg = CONNECTED_MG[r];
            int eg = CONNECTED_EG[r];
            // Standing side by side is worth more than being defended from
            // behind: the pair controls squares and advances together.
            if (phalanx) { mg += mg / 2; eg += eg / 2; }
            eg += SUPPORTED_EG * popcount(support);
            score.add(mg, eg);
            TRACE(T_CONNECTED + r, Us, phalanx ? 1.5 : 1.0);
            TRACE(T_SUPPORTED, Us, popcount(support));
        }

        if (passed) {
            e.passed[Us] |= square_bb(s);
            score.add(PASSED_PAWN_MG[r], PASSED_PAWN_EG[r]);
            TRACE(T_PASSED + r, Us, 1);
        }
    }

    e.score[Us] = score;
}

// Returns the pawn table entry for this position, filling it in on a miss.
// The two pawn bitboards are both the key and the verification, so an entry is
// only ever used for the exact structure it was computed from.
const PawnEntry* pawn_entry(const Position& pos) {
    const U64 white = pos.pieces(WHITE, PAWN);
    const U64 black = pos.pieces(BLACK, PAWN);

    // Mix both bitboards into an index.  Any mixing function will do; this one
    // is a multiply-xorshift, which spreads the bits cheaply.
    U64 key = white * 0x9E3779B97F4A7C15ULL ^ black * 0xC2B2AE3D27D4EB4FULL;
    key ^= key >> 29;
    key *= 0xBF58476D1CE4E5B9ULL;
    key ^= key >> 32;

    PawnEntry& e = pawn_table[key & (PAWN_TABLE_SIZE - 1)];
#ifndef EVAL_TRACE
    // (The tuner recomputes every time, so the trace sees the pawn terms.)
    if (e.pawns[WHITE] == white && e.pawns[BLACK] == black) return &e;
#endif

    e = PawnEntry{};
    e.pawns[WHITE] = white;
    e.pawns[BLACK] = black;
    compute_pawn_structure<WHITE>(pos, e);
    compute_pawn_structure<BLACK>(pos, e);
    return &e;
}

// ---------------------------------------------------------------------------
// One evaluation of one position.
//
// The terms are computed in a fixed order because they build on each other:
//
//   1. material and piece-square tables, and the game phase        (value)
//   2. pawn structure, from the pawn hash table                    (value)
//   3. per-piece terms, which also record every attacked square    (pieces)
//   4. king safety, which needs both sides' attack maps            (king_safety)
//   5. threats, which need both sides' attack maps                 (threats)
//   6. passed pawns, which need attack maps and king positions     (passed_pawns)
//   7. endgame scaling and the phase blend                         (value)
// ---------------------------------------------------------------------------

class Evaluation {
public:
    // Looks the pawn structure up (or computes it) straight away; everything
    // else happens in value().
    explicit Evaluation(const Position& p) : pos(p), pawns(pawn_entry(p)) {}

    // The finished evaluation, from the side to move's point of view.
    int value();

private:
    template<Color Us> void initialize();
    template<Color Us> Score pieces();
    template<Color Us> Score king_safety();
    template<Color Us> Score threats();
    template<Color Us> Score passed_pawns();
    template<Color Us> int king_shelter() const;
    template<Color Us> bool is_outpost(Square s) const;

    int material_and_psqt(Score& score) const;
    bool known_ending(int& white_score) const;
    int non_pawn_material(Color c) const;
    bool opposite_bishops() const;
    int scale_factor(int eg) const;

    const Position& pos;
    const PawnEntry* pawns;

    // attacked_by[c][pt]: squares attacked by that colour's pieces of that type.
    // Index ALL_PIECES holds the union over every piece of the colour.
    U64 attacked_by[COLOR_NB][PIECE_TYPE_NB + 1] = {};

    // Squares attacked at least twice: what makes a defence hold.
    U64 attacked_by2[COLOR_NB] = {};

    // Squares worth counting as mobility for each colour.
    U64 mobility_area[COLOR_NB] = {};

    // The king's square plus the ring around it, for both kings.
    U64 king_zone[COLOR_NB] = {};

    // How many pieces of a colour attack the *enemy* king's zone, and the
    // attack units they add up to.  Scored later, in king_safety().
    int king_attackers[COLOR_NB] = {};
    int king_attack_units[COLOR_NB] = {};
};

// Sets up the attack maps for pawns and the king, the king zone and the
// mobility area of one colour.  This runs before any piece is looked at,
// because the mobility area of one side depends on the other side's pawns.
//
// A square counts as mobility unless an enemy pawn covers it (a piece standing
// there could simply be taken), or our own king is on it, or it holds one of
// our pawns that is stuck: pawns on the first two ranks or with a piece
// directly in front cannot move out of the way to free the square.
template<Color Us>
void Evaluation::initialize() {
    constexpr Color Them = ~Us;
    const Square king = pos.king_square(Us);

    attacked_by[Us][PAWN] = pawns->attacks[Us];
    attacked_by[Us][KING] = king_attacks(king);
    attacked_by[Us][ALL_PIECES] = attacked_by[Us][PAWN] | attacked_by[Us][KING];
    attacked_by2[Us] = pawns->attacks2[Us] | (attacked_by[Us][PAWN] & attacked_by[Us][KING]);

    king_zone[Us] = king_attacks(king) | square_bb(king);

    const U64 stuck_pawns = pos.pieces(Us, PAWN)
                          & (LOW_RANKS[Us] | pawn_push<Them>(pos.pieces()));
    mobility_area[Us] = ~(pawns->attacks[Them] | stuck_pawns | square_bb(king));
}

// True when a piece on `s` is on an outpost: a square in the enemy half that
// one of our pawns defends and that no enemy pawn can ever attack, because
// none is left on the neighbouring files ahead of it.  A knight there cannot be
// chased away and is often worth more than a rook's exchange value.
template<Color Us>
bool Evaluation::is_outpost(Square s) const {
    return (OUTPOST_ZONE[Us] & square_bb(s))
        && (pawn_attacks(~Us, s) & pos.pieces(Us, PAWN))
        && !(ATTACK_SPAN[Us][s] & pos.pieces(~Us, PAWN));
}

// Walks every knight, bishop, rook and queen of one colour and scores what can
// be said about each in isolation: how many safe squares it reaches (mobility),
// whether a minor piece stands on an outpost, and whether a rook has an open
// file or the seventh rank.  On the way it fills in the attack maps and counts
// the attackers on the enemy king, which king_safety() and threats() use.
//
// A rook on the seventh only counts when there is something to attack there:
// the enemy king shut on its last rank, or enemy pawns still on the seventh.
template<Color Us>
Score Evaluation::pieces() {
    constexpr Color Them = ~Us;
    const U64 occupied = pos.pieces();
    const U64 own_pawns = pos.pieces(Us, PAWN);
    const U64 enemy_pawns = pos.pieces(Them, PAWN);
    Score score;

    for (PieceType pt : { KNIGHT, BISHOP, ROOK, QUEEN }) {
        U64 remaining = pos.pieces(Us, pt);
        while (remaining) {
            const Square s = pop_lsb(remaining);
            const U64 attacks = attacks_of(pt, s, occupied);

            // Record the squares before merging them, so that a square already
            // covered by another piece is marked as defended twice.
            attacked_by2[Us] |= attacked_by[Us][ALL_PIECES] & attacks;
            attacked_by[Us][pt] |= attacks;
            attacked_by[Us][ALL_PIECES] |= attacks;

            const int squares = popcount(attacks & mobility_area[Us]);
            const int moves = squares - MOBILITY_BASE[pt];
            score.add(moves * MOBILITY_MG[pt], moves * MOBILITY_EG[pt]);
            TRACE(T_MOBILITY + pt, Us, moves);

            if (pt == KNIGHT || pt == BISHOP) {
                if (square_bb(s) & pawn_push<Them>(own_pawns))
                    { score.add(MINOR_BEHIND_PAWN_MG, MINOR_BEHIND_PAWN_EG); TRACE(T_MINOR_BEHIND_PAWN, Us, 1); }
                const int distance = DISTANCE[s][pos.king_square(Us)];
                score.add(KING_PROTECTOR_MG[pt] * distance, KING_PROTECTOR_EG[pt] * distance);
                TRACE(T_KING_PROTECTOR + pt, Us, distance);
            }

            if (pt == BISHOP) {
                const int same_colour = popcount(own_pawns & ((DARK_SQUARES & square_bb(s)) ? DARK_SQUARES : ~DARK_SQUARES));
                score.add(BISHOP_PAWNS_MG * same_colour, BISHOP_PAWNS_EG * same_colour);
                TRACE(T_BISHOP_PAWNS, Us, same_colour);
                if (popcount(bishop_attacks(s, own_pawns | enemy_pawns) & CENTER) >= 2)
                    { score.add(LONG_DIAGONAL_BISHOP_MG, LONG_DIAGONAL_BISHOP_EG); TRACE(T_LONG_DIAGONAL, Us, 1); }
            }

            if (pt == ROOK && squares <= 3) {
                const int king_file = file_of(pos.king_square(Us));
                if ((king_file < 4) == (file_of(s) < king_file))
                    { score.add(TRAPPED_ROOK_MG, TRAPPED_ROOK_EG); TRACE(T_TRAPPED_ROOK, Us, 1); }
            }

            if (const U64 zone_hits = attacks & king_zone[Them]) {
                ++king_attackers[Us];
                king_attack_units[Us] += KING_ATTACK_WEIGHT[pt] * popcount(zone_hits);
            }

            if (pt == KNIGHT && is_outpost<Us>(s))
                { score.add(KNIGHT_OUTPOST_MG, KNIGHT_OUTPOST_EG); TRACE(T_KNIGHT_OUTPOST, Us, 1); }
            else if (pt == BISHOP && is_outpost<Us>(s))
                { score.add(BISHOP_OUTPOST_MG, BISHOP_OUTPOST_EG); TRACE(T_BISHOP_OUTPOST, Us, 1); }

            if (pt == ROOK) {
                const U64 file = FILE_BB[file_of(s)];
                if (!(file & own_pawns))
                    TRACE((file & enemy_pawns) ? T_ROOK_SEMI_OPEN : T_ROOK_OPEN, Us, 1),
                    score.add((file & enemy_pawns) ? ROOK_SEMI_OPEN_MG : ROOK_OPEN_FILE_MG,
                              (file & enemy_pawns) ? ROOK_SEMI_OPEN_EG : ROOK_OPEN_FILE_EG);

                if (relative_rank(Us, s) == 6
                    && (relative_rank(Us, pos.king_square(Them)) == 7
                        || (enemy_pawns & RANK_BB[rank_of(s)])))
                    { score.add(ROOK_ON_SEVENTH_MG, ROOK_ON_SEVENTH_EG); TRACE(T_ROOK_SEVENTH, Us, 1); }
            }
        }
    }

    // Two bishops cover both square colours between them, which is worth more
    // than the sum of the pieces, and worth more still as the board empties.
    if (popcount(pos.pieces(Us, BISHOP)) >= 2)
        { score.add(BISHOP_PAIR_MG, BISHOP_PAIR_EG); TRACE(T_BISHOP_PAIR, Us, 1); }

    return score;
}

// The pawn cover in front of our own king: for the king's file and the files
// beside it, whether a pawn stands directly in front of the king (or one square
// further up), and whether the file is open or half-open towards enemy rooks.
// Middlegame only -- an exposed king in a pawn endgame is usually a good king.
template<Color Us>
int Evaluation::king_shelter() const {
    const Square king = pos.king_square(Us);
    const U64 own_pawns = pos.pieces(Us, PAWN);
    const U64 enemy_pawns = pos.pieces(~Us, PAWN);
    const int up = (Us == WHITE) ? 1 : -1;
    const int king_file = file_of(king);
    const int king_rank = rank_of(king);

    int mg = 0;
    for (int f = std::max(0, king_file - 1); f <= std::min(7, king_file + 1); ++f) {
        const int one_up = king_rank + up;
        const int two_up = king_rank + 2 * up;
        const bool shield = one_up >= 0 && one_up <= 7
                         && (own_pawns & square_bb(make_square(f, one_up)));
        const bool advanced = two_up >= 0 && two_up <= 7
                           && (own_pawns & square_bb(make_square(f, two_up)));
        if (!shield)
            mg += advanced ? SHIELD_PAWN_ADVANCED : SHIELD_PAWN_MISSING;
        if (!shield)
            TRACE(advanced ? T_SHIELD_ADVANCED : T_SHIELD_MISSING, Us, 1);

        if (!(own_pawns & FILE_BB[f]))
            mg += (enemy_pawns & FILE_BB[f]) ? KING_FILE_SEMI_OPEN : KING_FILE_OPEN;
        if (!(own_pawns & FILE_BB[f]))
            TRACE((enemy_pawns & FILE_BB[f]) ? T_KING_FILE_SEMI_OPEN : T_KING_FILE_OPEN, Us, 1);

        // The enemy pawn on this file closest to our king, among those in front of it.
        U64 in_front = 0;
        for (int r = king_rank + up; r >= 0 && r <= 7; r += up) in_front |= RANK_BB[r];
        if (const U64 stormers = enemy_pawns & FILE_BB[f] & in_front) {
            const Square closest = (Us == WHITE) ? lsb(stormers) : msb(stormers);
            const int r = relative_rank(Us, closest);
            mg += PAWN_STORM[r];
            TRACE(T_PAWN_STORM + r, Us, 1);
        }
    }
    return mg;
}

// The danger our own king is in, as a penalty for us: the shelter in front of
// it, plus what the enemy pieces aim at it.
//
// A lone attacker rarely breaks through, so the attack part only counts from
// two attackers up.  Squares in the king's zone that the enemy attacks and we
// do not defend twice are added as extra attack units: those are the squares a
// piece can land on and survive.
template<Color Us>
Score Evaluation::king_safety() {
    constexpr Color Them = ~Us;
    Score score;
    score.add(king_shelter<Us>(), 0);

    // Safe checks: squares the enemy could check from without being taken.
    {
        const Square king = pos.king_square(Us);
        const U64 occupied = pos.pieces();
        const U64 safe = ~attacked_by[Us][ALL_PIECES] & ~pos.pieces(Them);
        const U64 diagonal = bishop_attacks(king, occupied), straight = rook_attacks(king, occupied);
        const U64 checks[PIECE_TYPE_NB] = {
            0, knight_attacks(king), diagonal, straight, diagonal | straight, 0 };
        for (PieceType pt : { KNIGHT, BISHOP, ROOK, QUEEN }) {
            const int n = popcount(checks[pt] & attacked_by[Them][pt] & safe);
            score.add(SAFE_CHECK_MG[pt] * n, SAFE_CHECK_EG[pt] * n);
            TRACE(T_SAFE_CHECK + pt, Us, n);
        }
    }

    if (king_attackers[Them] < 2) return score;

    const U64 weak = king_zone[Us] & attacked_by[Them][ALL_PIECES] & ~attacked_by2[Us];
    const int units = king_attack_units[Them] + KING_ZONE_WEAK_SQUARE * popcount(weak);

    int danger = std::min(units * units / 4, KING_DANGER_MAX);
    danger = danger * KING_ATTACKER_SCALE[std::min(king_attackers[Them], 7)] / 100;
    score.add(-danger, 0);
    return score;
}

// What we are attacking that the opponent would rather we were not.  A threat
// is not a win of material -- it is the opponent's move -- but it costs them
// time and limits what else they can do, so it is worth part of the difference.
//
//  * a pawn attacking a piece is the cheapest threat there is: the piece must
//    move or be defended enough times to survive a pawn taking it;
//  * a knight or bishop attacking a rook or queen wins the exchange if ignored;
//  * a rook attacking the queen forces her to move;
//  * a piece that we attack and nothing defends is hanging, whatever it is.
template<Color Us>
Score Evaluation::threats() {
    constexpr Color Them = ~Us;
    Score score;

    // Only pieces: pawns are too cheap to be worth threatening, and the king
    // cannot be captured.
    const U64 their_pieces = pos.pieces(Them)
                           & ~pos.pieces(Them, PAWN) & ~pos.pieces(Them, KING);

    U64 threatened = attacked_by[Us][PAWN] & their_pieces;
    while (threatened) {
        const PieceType victim = type_of(pos.piece_on(pop_lsb(threatened)));
        score.add(THREAT_BY_PAWN_MG[victim], THREAT_BY_PAWN_EG[victim]);
        TRACE(T_THREAT_BY_PAWN + victim, Us, 1);
    }

    threatened = (attacked_by[Us][KNIGHT] | attacked_by[Us][BISHOP])
               & (pos.pieces(Them, ROOK) | pos.pieces(Them, QUEEN));
    while (threatened) {
        const PieceType victim = type_of(pos.piece_on(pop_lsb(threatened)));
        score.add(THREAT_BY_MINOR_MG[victim], THREAT_BY_MINOR_EG[victim]);
        TRACE(T_THREAT_BY_MINOR + victim, Us, 1);
    }

    if (attacked_by[Us][ROOK] & pos.pieces(Them, QUEEN))
        { score.add(THREAT_BY_ROOK_MG, THREAT_BY_ROOK_EG); TRACE(T_THREAT_BY_ROOK, Us, 1); }

    const int hanging = popcount(their_pieces & attacked_by[Us][ALL_PIECES]
                                 & ~attacked_by[Them][ALL_PIECES]);
    score.add(HANGING_MG * hanging, HANGING_EG * hanging);
    TRACE(T_HANGING, Us, hanging);

    return score;
}

// Everything about a passed pawn that the pawn hash table cannot know, because
// it depends on the pieces and the kings.  All of it is endgame-only: in the
// middlegame the rank bonus already stored in the table is enough.
//
//  * king distance -- the closer our king is to the square in front of the pawn
//    (and the further theirs), the more likely the pawn survives to queen;
//  * a free path (nothing at all in the way) is worth more than a pawn with a
//    piece parked in front of it, and a path the opponent does not attack at
//    all is worth more still;
//  * a rook behind the pawn defends every square it advances to, so it supports
//    the pawn all the way in; an enemy rook there does the opposite;
//  * the rule of the square -- with no enemy pieces left, count whether their
//    king reaches the queening square before the pawn does.  If it cannot, the
//    pawn is worth nearly a queen.
template<Color Us>
Score Evaluation::passed_pawns() {
    constexpr Color Them = ~Us;
    const U64 occupied = pos.pieces();
    const Square our_king = pos.king_square(Us);
    const Square their_king = pos.king_square(Them);
    Score score;

    U64 passers = pawns->passed[Us];
    while (passers) {
        const Square s = pop_lsb(passers);
        const int r = relative_rank(Us, s);
        const Square stop = Square(int(s) + push_delta(Us));
        const U64 path = FORWARD_FILE[Us][s];
        int eg = 0;

        if (const int weight = PASSED_KING_WEIGHT[r]) {
            eg += DISTANCE[their_king][stop] * weight;
            eg -= DISTANCE[our_king][stop] * weight * 2 / 3;
            TRACE(T_PASSED_KING + r, Us, DISTANCE[their_king][stop] - DISTANCE[our_king][stop] * 2.0 / 3.0);
        }

        if (!(path & occupied)) {
            eg += PASSED_FREE_EG[r];
            TRACE(T_PASSED_FREE + r, Us, 1);
            if (!(path & attacked_by[Them][ALL_PIECES])) { eg += PASSED_SAFE_EG[r]; TRACE(T_PASSED_SAFE + r, Us, 1); }
        } else if (pos.pieces(Them) & square_bb(stop)) {
            eg += PASSED_BLOCKED_EG[r];
            TRACE(T_PASSED_BLOCKED + r, Us, 1);
        }

        // Squares behind the pawn on its own file, as far as a rook standing
        // there could actually see.
        const U64 behind = FORWARD_FILE[Them][s] & rook_attacks(s, occupied);
        if (behind & pos.pieces(Us, ROOK)) { eg += ROOK_BEHIND_PASSER_EG; TRACE(T_ROOK_BEHIND_PASSER, Us, 1); }
        else if (behind & pos.pieces(Them, ROOK)) { eg -= ROOK_BEHIND_PASSER_EG; TRACE(T_ROOK_BEHIND_PASSER, Us, -1); }

        if (!non_pawn_material(Them) && !(path & occupied)) {
            const Square promotion = make_square(file_of(s), (Us == WHITE) ? 7 : 0);
            // A pawn still on its starting rank saves a move with the double push.
            const int pawn_steps = 7 - r - (r == 1 ? 1 : 0);
            // Moving first is worth a move of head start to the defender.
            const int king_steps = DISTANCE[their_king][promotion]
                                 - (pos.side_to_move() == Them ? 1 : 0);
            if (king_steps > pawn_steps) { eg += PASSED_UNSTOPPABLE_EG; TRACE(T_PASSED_UNSTOPPABLE, Us, 1); }
        }

        score.add(0, eg);
    }

    return score;
}

// Material and piece-square values for every piece on the board, White positive
// and Black negative, in one sweep.  Returns the game phase counted in the same
// pass: 24 with all the pieces on, 0 with nothing but kings and pawns.
int Evaluation::material_and_psqt(Score& score) const {
    int phase = 0;
    for (int s = SQ_A1; s <= SQ_H8; ++s) {
        const Piece pc = pos.piece_on(Square(s));
        if (pc == NO_PIECE) continue;

        phase += PHASE_WEIGHT[type_of(pc)];
        if (color_of(pc) == WHITE) score.add(MG_PSQ[pc][s], EG_PSQ[pc][s]);
        else score.sub(MG_PSQ[pc][s], EG_PSQ[pc][s]);
        TRACE(T_MATERIAL + type_of(pc), color_of(pc), 1);
        TRACE(T_PSQT + type_of(pc) * 64 + (color_of(pc) == WHITE ? s ^ 56 : s), color_of(pc), 1);
    }
    return std::min(phase, MAX_PHASE);
}

// Middlegame value of everything a colour owns except pawns and the king: the
// material that can actually force a win.
int Evaluation::non_pawn_material(Color c) const {
    int total = 0;
    for (PieceType pt : { KNIGHT, BISHOP, ROOK, QUEEN })
        total += MG_MATERIAL[pt] * popcount(pos.pieces(c, pt));
    return total;
}

// True when each side has exactly one bishop and the two travel on squares of
// different colours -- neither bishop can ever attack or defend what the other
// one covers, which is what makes these endings so hard to win.
bool Evaluation::opposite_bishops() const {
    const U64 white = pos.pieces(WHITE, BISHOP);
    const U64 black = pos.pieces(BLACK, BISHOP);
    if (popcount(white) != 1 || popcount(black) != 1) return false;
    return bool(white & DARK_SQUARES) != bool(black & DARK_SQUARES);
}

// How much of the endgame score to believe, out of SCALE_NORMAL.
//
// The evaluation counts material and structure, but some endings simply cannot
// be won however good they look: bishops of opposite colours let the defender
// give the bishop for the last pawn; a side with no pawns at all needs a clear
// rook's worth of extra material before a win exists; and knights without pawns
// cannot force mate at all.  `eg` decides who is the stronger side; only that
// side's winning chances are scaled down.
int Evaluation::scale_factor(int eg) const {
    const Color strong = (eg > 0) ? WHITE : BLACK;
    const int strong_pawns = popcount(pos.pieces(strong, PAWN));
    int scale = SCALE_NORMAL;

    if (opposite_bishops()) {
        const bool bishops_only = non_pawn_material(WHITE) == MG_MATERIAL[BISHOP]
                               && non_pawn_material(BLACK) == MG_MATERIAL[BISHOP];
        scale = bishops_only ? SCALE_OPPOSITE_BISHOPS : SCALE_OPPOSITE_BISHOPS_PIECES;
    }

    if (strong_pawns == 0) {
        const int edge = non_pawn_material(strong) - non_pawn_material(~strong);
        const bool knights_only = !pos.pieces(strong, BISHOP) && !pos.pieces(strong, ROOK)
                               && !pos.pieces(strong, QUEEN);

        // Knights alone cannot force mate, however many of them there are.
        if (knights_only) scale = std::min(scale, SCALE_NO_PAWNS_TINY_EDGE);
        else if (edge <= MG_MATERIAL[BISHOP]) scale = std::min(scale, SCALE_NO_PAWNS_TINY_EDGE);
        else if (edge <= MG_MATERIAL[ROOK]) scale = std::min(scale, SCALE_NO_PAWNS_SMALL_EDGE);
    }

    // One pawn against nothing but a matching piece, with the defending king
    // standing in the pawn's path: the classic drawing setups.
    if (strong_pawns == 1 && !pos.pieces(~strong, PAWN)) {
        const Square pawn = lsb(pos.pieces(strong, PAWN));
        const Square weak_king = pos.king_square(~strong);
        const bool king_in_path = file_of(weak_king) == file_of(pawn)
                               && relative_rank(strong, weak_king) > relative_rank(strong, pawn);
        auto only = [&](Color c, PieceType pt) {
            return popcount(pos.pieces(c, pt)) == 1
                && popcount(pos.pieces(c) & ~pos.pieces(c, PAWN) & ~pos.pieces(c, KING)) == 1;
        };

        // Rook and pawn against rook: with the king in front, the defence holds
        // (Philidor) far more often than not.
        if (king_in_path && only(strong, ROOK) && only(~strong, ROOK))
            scale = std::min(scale, SCALE_BLOCKED_ROOK_PAWN);

        // Bishop and pawn against bishop: a king in front on a square the strong
        // side's bishop cannot reach can never be driven away.
        if (king_in_path && only(strong, BISHOP) && only(~strong, BISHOP)
            && bool(pos.pieces(strong, BISHOP) & DARK_SQUARES) != bool(square_bb(weak_king) & DARK_SQUARES))
            scale = std::min(scale, SCALE_BLOCKED_BISHOP_PAWN);
    }

    return scale;
}

// Endings whose result is known, scored directly from White's point of view.
// All of them are a lone king against something:
//
//  * king and pawn against king -- looked up in the exact table (bitbase.cpp);
//  * rook pawns only, with the defending king in front of them -- a draw;
//  * bishop and rook pawns, where the bishop does not control the queening
//    square and the defending king reaches it -- a draw ("wrong bishop");
//  * bishop and knight -- a win, but only in a corner of the bishop's colour,
//    so the lone king is driven there;
//  * anything else that can force mate (a queen, a rook, bishop and knight, two
//    bishops on different colours) -- a win, driving the lone king to the edge
//    and bringing our own king up, which is how the mate is found.
//
// Returns false for everything else, which is evaluated normally.
bool Evaluation::known_ending(int& white_score) const {
    for (Color strong : { WHITE, BLACK }) {
        const Color weak = ~strong;
        if (pos.pieces(weak) != pos.pieces(weak, KING)) continue;   // the weak side has more than a king

        const int pawns = popcount(pos.pieces(strong, PAWN));
        const int knights = popcount(pos.pieces(strong, KNIGHT));
        const int bishops = popcount(pos.pieces(strong, BISHOP));
        const int rooks = popcount(pos.pieces(strong, ROOK));
        const int queens = popcount(pos.pieces(strong, QUEEN));
        const Square strong_king = pos.king_square(strong);
        const Square weak_king = pos.king_square(weak);
        const int sign = (strong == WHITE) ? 1 : -1;

        // A lone king with no move and not in check is stalemated.  Quiescence
        // search does not look for stalemate, so without this a won ending could
        // be thrown away on the last move of a line.
        if (pos.side_to_move() == weak && !pos.in_check()) {
            MoveList moves;
            generate_legal(pos, moves);
            if (moves.empty()) { white_score = 0; return true; }
        }

        // Every pawn on one rook file, and which one.
        const U64 pawn_set = pos.pieces(strong, PAWN);
        const bool rook_file_pawns = pawns > 0 && (!(pawn_set & ~FILE_A_BB) || !(pawn_set & ~FILE_H_BB));
        const Square queening = rook_file_pawns
            ? make_square(file_of(lsb(pawn_set)), strong == WHITE ? 7 : 0) : SQ_A1;   // (unused without rook-file pawns)

        if (pawns == 1 && knights + bishops + rooks + queens == 0) {
            const Square pawn = lsb(pawn_set);
            const bool win = Bitbase::kpk_win(strong, strong_king, pawn, weak_king, pos.side_to_move());
            white_score = win ? sign * (KNOWN_WIN + EG_MATERIAL[PAWN] + 10 * relative_rank(strong, pawn)) : 0;
            return true;
        }

        if (rook_file_pawns && knights + bishops + rooks + queens == 0) {
            const U64 front = (strong == WHITE) ? square_bb(msb(pawn_set)) : square_bb(lsb(pawn_set));
            if (DISTANCE[weak_king][queening] <= 1
                && relative_rank(strong, weak_king) > relative_rank(strong, lsb(front))) {
                white_score = 0;
                return true;
            }
            return false;
        }

        if (rook_file_pawns && bishops == 1 && knights + rooks + queens == 0) {
            const bool bishop_dark = pos.pieces(strong, BISHOP) & DARK_SQUARES;
            const bool corner_dark = square_bb(queening) & DARK_SQUARES;
            if (bishop_dark != corner_dark && DISTANCE[weak_king][queening] <= 1) {
                white_score = 0;
                return true;
            }
            return false;
        }

        if (pawns == 0 && knights == 1 && bishops == 1 && rooks + queens == 0) {
            const bool dark = pos.pieces(strong, BISHOP) & DARK_SQUARES;
            const Square a = dark ? SQ_A1 : SQ_A8, b = dark ? SQ_H8 : SQ_H1;
            // Mate is only possible in a corner of the bishop's colour: pull the
            // lone king towards the nearer of the two, step by step along the edge.
            const int corner = std::min(steps(weak_king, a), steps(weak_king, b));
            // The pull has to be steep -- each step towards the right corner worth
            // more than any king manoeuvre -- or the lone king is left in the centre.
            white_score = sign * (KNOWN_WIN + push_close(DISTANCE[strong_king][weak_king])
                                  + 320 * (14 - corner) + push_to_edge(weak_king));
            return true;
        }

        const bool both_bishop_colours = (pos.pieces(strong, BISHOP) & DARK_SQUARES)
                                      && (pos.pieces(strong, BISHOP) & ~DARK_SQUARES);
        if (queens || rooks || (bishops && knights) || both_bishop_colours) {
            int material = 0;
            for (PieceType pt : { PAWN, KNIGHT, BISHOP, ROOK, QUEEN })
                material += EG_MATERIAL[pt] * popcount(pos.pieces(strong, pt));
            white_score = sign * (KNOWN_WIN + material + push_to_edge(weak_king)
                                  + push_close(DISTANCE[strong_king][weak_king]));
            return true;
        }
        return false;
    }
    return false;
}

// Runs every term in the order they depend on each other, scales the endgame
// score, blends the two phases and returns the result from the side to move's
// point of view.
//
// The blend is a straight interpolation: with all the pieces on the board the
// middlegame score is used, with none of them the endgame score, and in between
// a proportional mix.  One number cannot say both "the king wants shelter" and
// "the king wants the centre", so two are kept until the last moment.
int Evaluation::value() {
#ifndef EVAL_TRACE
    // Endings whose result is known are scored by what they are worth, not by
    // the terms below.  (The tuner leaves them out: they have no weights.)
    if (int white_score = 0; known_ending(white_score))
        return pos.side_to_move() == WHITE ? white_score : -white_score;
#endif

    Score score;
    const int phase = material_and_psqt(score);

    initialize<WHITE>();
    initialize<BLACK>();

    // Pawn structure: cached, and already a White-minus-Black difference here.
    score.add(pawns->score[WHITE].mg - pawns->score[BLACK].mg,
              pawns->score[WHITE].eg - pawns->score[BLACK].eg);

    // Both sides' pieces first: king safety and threats read the attack maps of
    // both colours, so neither can be scored until both passes are done.
    Score white = pieces<WHITE>();
    Score black = pieces<BLACK>();

    white += king_safety<WHITE>();
    black += king_safety<BLACK>();
    white += threats<WHITE>();
    black += threats<BLACK>();
    white += passed_pawns<WHITE>();
    black += passed_pawns<BLACK>();

    score.add(white.mg - black.mg, white.eg - black.eg);

    const int scale = scale_factor(score.eg);
    const int eg = score.eg * scale / SCALE_NORMAL;
    const int blended = (score.mg * phase + eg * (MAX_PHASE - phase)) / MAX_PHASE;
#ifdef EVAL_TRACE
    trace_totals = { score.mg, score.eg, phase, scale };
#endif

    // Negamax wants the score from the mover's point of view, and having the
    // move is itself worth something.
    // The fifty-move fade is applied by fifty_move_scale(), not here, so that
    // the transposition table can store this clock-independent value.
    return (pos.side_to_move() == WHITE ? blended : -blended) + TEMPO;
}

} // namespace

// Fills in the piece-square lookup, the pawn-structure masks, the outpost zones
// and the square-distance table.  Everything here depends only on the board
// geometry, so it is computed once and never changes.
void init() {
    Bitbase::init();

    for (int c = WHITE; c <= BLACK; ++c) {
        for (int pt = PAWN; pt < PIECE_TYPE_NB; ++pt) {
            Piece pc = make_piece(Color(c), PieceType(pt));
            for (int s = SQ_A1; s <= SQ_H8; ++s) {
                // White reads the table flipped (index 0 is a8); Black reads it
                // directly, which mirrors the square vertically.
                int index = (c == WHITE) ? (s ^ 56) : s;
                MG_PSQ[pc][s] = MG_MATERIAL[pt] + MG_TABLE[pt][index];
                EG_PSQ[pc][s] = EG_MATERIAL[pt] + EG_TABLE[pt][index];
            }
        }
    }

    for (int f = 0; f < 8; ++f) {
        FILE_NEIGHBOURS[f] = 0;
        if (f > 0) FILE_NEIGHBOURS[f] |= FILE_BB[f - 1];
        if (f < 7) FILE_NEIGHBOURS[f] |= FILE_BB[f + 1];
    }

    for (int s = SQ_A1; s <= SQ_H8; ++s) {
        Square sq = Square(s);
        int f = file_of(sq), r = rank_of(sq);

        // Every rank ahead of this square, for each colour's point of view.
        U64 white_front = 0, black_front = 0;
        for (int rr = r + 1; rr <= 7; ++rr) white_front |= RANK_BB[rr];
        for (int rr = r - 1; rr >= 0; --rr) black_front |= RANK_BB[rr];

        FORWARD_FILE[WHITE][s] = white_front & FILE_BB[f];
        FORWARD_FILE[BLACK][s] = black_front & FILE_BB[f];

        PASSED_MASK[WHITE][s] = white_front & (FILE_BB[f] | FILE_NEIGHBOURS[f]);
        PASSED_MASK[BLACK][s] = black_front & (FILE_BB[f] | FILE_NEIGHBOURS[f]);

        ATTACK_SPAN[WHITE][s] = white_front & FILE_NEIGHBOURS[f];
        ATTACK_SPAN[BLACK][s] = black_front & FILE_NEIGHBOURS[f];

        // The neighbouring files from this square backwards, including its own
        // rank: where a pawn that could defend this one would have to stand.
        REAR_SPAN[WHITE][s] = ~white_front & FILE_NEIGHBOURS[f];
        REAR_SPAN[BLACK][s] = ~black_front & FILE_NEIGHBOURS[f];

        for (int t = SQ_A1; t <= SQ_H8; ++t)
            DISTANCE[s][t] = std::max(std::abs(f - file_of(Square(t))),
                                      std::abs(r - rank_of(Square(t))));
    }

    // An outpost is only worth having in the enemy half: relative ranks 4 to 6.
    OUTPOST_ZONE[WHITE] = RANK_4_BB | RANK_5_BB | RANK_6_BB;
    OUTPOST_ZONE[BLACK] = RANK_5_BB | RANK_4_BB | RANK_3_BB;
}

void set_fifty_move_scaling(bool on) { fifty_move_scaling = on; }

// Empties the calling thread's pawn hash table, so a new game starts with
// nothing cached.  Other threads keep theirs: every entry is checked against
// the actual pawns before it is used, so a stale one is never wrong, only
// useless.
void clear() {
    for (PawnEntry& e : pawn_table) e = PawnEntry{};
}

// The engine's static evaluation: builds one Evaluation and asks it for the
// score.  Every call is independent; nothing but the pawn hash table survives
// between calls.
int evaluate_unscaled(const Position& pos) {
    return Evaluation(pos).value();
}

// Whatever either side has built up is worth less as the fifty-move clock runs
// down: a position two moves from a draw by rule is nearly a draw, whatever the
// material says.  Shuffling pieces therefore costs the better side part of its
// advantage, while a pawn move or a capture resets the clock and hands it
// straight back -- which is the incentive to make progress instead of going
// round in circles.
int fifty_move_scale(int score, const Position& pos) {
    if (!fifty_move_scaling) return score;
    const int clock = std::clamp(pos.halfmove_clock(), 0, 100);
    return score * (200 - clock) / 200;
}

int evaluate(const Position& pos) {
    return fifty_move_scale(evaluate_unscaled(pos), pos);
}

#ifdef EVAL_TRACE
namespace Tune {

namespace {

const char* const PIECE_NAMES[PIECE_TYPE_NB] = { "PAWN", "KNIGHT", "BISHOP", "ROOK", "QUEEN", "KING" };

// Fills `params` from the weights as they stand.  The order follows TraceIndex.
void collect(std::vector<Param>& params) {
    params.assign(T_COUNT, Param{});
    auto set = [&](int index, const std::string& name, int mg, int eg, bool tune_mg = true, bool tune_eg = true) {
        params[index] = Param{ name, mg, eg, tune_mg, tune_eg };
    };
    auto unused = [&](int index, const std::string& name) { set(index, name, 0, 0, false, false); };

    for (int pt = PAWN; pt < PIECE_TYPE_NB; ++pt) {
        if (pt == KING) unused(T_MATERIAL + pt, "MATERIAL_KING");
        else set(T_MATERIAL + pt, std::string("MATERIAL_") + PIECE_NAMES[pt], MG_MATERIAL[pt], EG_MATERIAL[pt]);
        for (int i = 0; i < 64; ++i) {
            const bool pawn_edge = pt == PAWN && (i < 8 || i >= 56);   // pawns never stand on ranks 1 and 8
            set(T_PSQT + pt * 64 + i, std::string(PIECE_NAMES[pt]) + "_" + std::to_string(i),
                MG_TABLE[pt][i], EG_TABLE[pt][i], !pawn_edge, !pawn_edge);
        }
    }
    set(T_BISHOP_PAIR, "BISHOP_PAIR", BISHOP_PAIR_MG, BISHOP_PAIR_EG);
    set(T_DOUBLED, "DOUBLED_PAWN", DOUBLED_PAWN_MG, DOUBLED_PAWN_EG);
    set(T_ISOLATED, "ISOLATED_PAWN", ISOLATED_PAWN_MG, ISOLATED_PAWN_EG);
    set(T_BACKWARD, "BACKWARD_PAWN", BACKWARD_PAWN_MG, BACKWARD_PAWN_EG);
    for (int r = 0; r < 8; ++r) {
        const bool inner = r >= 1 && r <= 6;
        set(T_CONNECTED + r, "CONNECTED_" + std::to_string(r), CONNECTED_MG[r], CONNECTED_EG[r], inner, inner);
        set(T_PASSED + r, "PASSED_" + std::to_string(r), PASSED_PAWN_MG[r], PASSED_PAWN_EG[r], inner, inner);
        set(T_PASSED_FREE + r, "PASSED_FREE_" + std::to_string(r), 0, PASSED_FREE_EG[r], false, inner);
        set(T_PASSED_SAFE + r, "PASSED_SAFE_" + std::to_string(r), 0, PASSED_SAFE_EG[r], false, inner);
        set(T_PASSED_BLOCKED + r, "PASSED_BLOCKED_" + std::to_string(r), 0, PASSED_BLOCKED_EG[r], false, inner);
        set(T_PASSED_KING + r, "PASSED_KING_" + std::to_string(r), 0, PASSED_KING_WEIGHT[r], false, inner);
    }
    set(T_SUPPORTED, "SUPPORTED", 0, SUPPORTED_EG, false, true);
    set(T_ROOK_BEHIND_PASSER, "ROOK_BEHIND_PASSER", 0, ROOK_BEHIND_PASSER_EG, false, true);
    set(T_PASSED_UNSTOPPABLE, "PASSED_UNSTOPPABLE", 0, PASSED_UNSTOPPABLE_EG, false, true);
    for (int pt = PAWN; pt < PIECE_TYPE_NB; ++pt) {
        const bool piece = pt >= KNIGHT && pt <= QUEEN;
        set(T_MOBILITY + pt, std::string("MOBILITY_") + PIECE_NAMES[pt], MOBILITY_MG[pt], MOBILITY_EG[pt], piece, piece);
        set(T_KING_PROTECTOR + pt, std::string("KING_PROTECTOR_") + PIECE_NAMES[pt],
            KING_PROTECTOR_MG[pt], KING_PROTECTOR_EG[pt], pt == KNIGHT || pt == BISHOP, pt == KNIGHT || pt == BISHOP);
        set(T_SAFE_CHECK + pt, std::string("SAFE_CHECK_") + PIECE_NAMES[pt], SAFE_CHECK_MG[pt], SAFE_CHECK_EG[pt], piece, piece);
        set(T_THREAT_BY_PAWN + pt, std::string("THREAT_BY_PAWN_") + PIECE_NAMES[pt],
            THREAT_BY_PAWN_MG[pt], THREAT_BY_PAWN_EG[pt], piece, piece);
        const bool heavy = pt == ROOK || pt == QUEEN;
        set(T_THREAT_BY_MINOR + pt, std::string("THREAT_BY_MINOR_") + PIECE_NAMES[pt],
            THREAT_BY_MINOR_MG[pt], THREAT_BY_MINOR_EG[pt], heavy, heavy);
    }
    set(T_MINOR_BEHIND_PAWN, "MINOR_BEHIND_PAWN", MINOR_BEHIND_PAWN_MG, MINOR_BEHIND_PAWN_EG);
    set(T_BISHOP_PAWNS, "BISHOP_PAWNS", BISHOP_PAWNS_MG, BISHOP_PAWNS_EG);
    set(T_LONG_DIAGONAL, "LONG_DIAGONAL_BISHOP", LONG_DIAGONAL_BISHOP_MG, LONG_DIAGONAL_BISHOP_EG);
    set(T_TRAPPED_ROOK, "TRAPPED_ROOK", TRAPPED_ROOK_MG, TRAPPED_ROOK_EG);
    for (int r = 0; r < 8; ++r)
        set(T_PAWN_STORM + r, "PAWN_STORM_" + std::to_string(r), PAWN_STORM[r], 0, r >= 1 && r <= 6, false);
    set(T_ROOK_OPEN, "ROOK_OPEN_FILE", ROOK_OPEN_FILE_MG, ROOK_OPEN_FILE_EG);
    set(T_ROOK_SEMI_OPEN, "ROOK_SEMI_OPEN", ROOK_SEMI_OPEN_MG, ROOK_SEMI_OPEN_EG);
    set(T_ROOK_SEVENTH, "ROOK_ON_SEVENTH", ROOK_ON_SEVENTH_MG, ROOK_ON_SEVENTH_EG);
    set(T_KNIGHT_OUTPOST, "KNIGHT_OUTPOST", KNIGHT_OUTPOST_MG, KNIGHT_OUTPOST_EG);
    set(T_BISHOP_OUTPOST, "BISHOP_OUTPOST", BISHOP_OUTPOST_MG, BISHOP_OUTPOST_EG);
    set(T_THREAT_BY_ROOK, "THREAT_BY_ROOK", THREAT_BY_ROOK_MG, THREAT_BY_ROOK_EG);
    set(T_HANGING, "HANGING", HANGING_MG, HANGING_EG);
    set(T_SHIELD_ADVANCED, "SHIELD_PAWN_ADVANCED", SHIELD_PAWN_ADVANCED, 0, true, false);
    set(T_SHIELD_MISSING, "SHIELD_PAWN_MISSING", SHIELD_PAWN_MISSING, 0, true, false);
    set(T_KING_FILE_SEMI_OPEN, "KING_FILE_SEMI_OPEN", KING_FILE_SEMI_OPEN, 0, true, false);
    set(T_KING_FILE_OPEN, "KING_FILE_OPEN", KING_FILE_OPEN, 0, true, false);
}

int round_int(double x) { return int(std::lround(x)); }

std::string array_line(const std::string& decl, const std::vector<int>& values) {
    std::string out = decl + " = { ";
    for (size_t i = 0; i < values.size(); ++i) out += (i ? ", " : "") + std::to_string(values[i]);
    return out + " };\n";
}

std::string board(const std::string& name, const std::vector<int>& values) {
    std::string out = "const int " + name + "[64] = {\n";
    for (int row = 0; row < 8; ++row) {
        out += "    ";
        for (int col = 0; col < 8; ++col) {
            char cell[8];
            std::snprintf(cell, sizeof cell, "%4d", values[row * 8 + col]);
            out += cell;
            if (row * 8 + col < 63) out += ",";
        }
        out += "\n";
    }
    return out + "};\n";
}

} // namespace

std::vector<Param> parameters() {
    std::vector<Param> params;
    collect(params);
    return params;
}

void trace(const Position& pos, Trace& out) {
    std::fill(std::begin(trace_coefficients), std::end(trace_coefficients), 0.0);
    const int stm_score = Evaluation(pos).value();
    out.coefficients.clear();
    for (int i = 0; i < T_COUNT; ++i)
        if (trace_coefficients[i] != 0.0) out.coefficients.emplace_back(i, trace_coefficients[i]);
    out.mg = trace_totals.mg;
    out.eg = trace_totals.eg;
    out.phase = trace_totals.phase;
    out.scale = trace_totals.scale;
    out.tempo = pos.side_to_move() == WHITE ? TEMPO : -TEMPO;
    out.white_score = pos.side_to_move() == WHITE ? stm_score : -stm_score;
}

std::string source(const std::vector<double>& mg_in, const std::vector<double>& eg_in) {
    std::vector<double> mg = mg_in, eg = eg_in;

    // A constant added to every square of a piece's table is the same as adding
    // it to the piece's material, so move each table's average into material
    // and keep the tables centred on zero.
    for (int pt = PAWN; pt <= QUEEN; ++pt) {
        double sum_mg = 0, sum_eg = 0;
        int count = 0;
        for (int i = 0; i < 64; ++i) {
            if (pt == PAWN && (i < 8 || i >= 56)) continue;
            sum_mg += mg[T_PSQT + pt * 64 + i];
            sum_eg += eg[T_PSQT + pt * 64 + i];
            ++count;
        }
        const double avg_mg = sum_mg / count, avg_eg = sum_eg / count;
        mg[T_MATERIAL + pt] += avg_mg;
        eg[T_MATERIAL + pt] += avg_eg;
        for (int i = 0; i < 64; ++i) {
            if (pt == PAWN && (i < 8 || i >= 56)) continue;
            mg[T_PSQT + pt * 64 + i] -= avg_mg;
            eg[T_PSQT + pt * 64 + i] -= avg_eg;
        }
    }

    auto vec = [&](const std::vector<double>& v, int start, int n) {
        std::vector<int> out;
        for (int i = 0; i < n; ++i) out.push_back(round_int(v[start + i]));
        return out;
    };
    auto scalar = [&](const std::string& name, double v) {
        return "constexpr int " + name + " = " + std::to_string(round_int(v)) + ";\n";
    };

    std::vector<int> mg_material = vec(mg, T_MATERIAL, PIECE_TYPE_NB), eg_material = vec(eg, T_MATERIAL, PIECE_TYPE_NB);
    mg_material[KING] = eg_material[KING] = 0;
    std::string out;
    out += array_line("const int MG_MATERIAL[PIECE_TYPE_NB]", mg_material);
    out += array_line("const int EG_MATERIAL[PIECE_TYPE_NB]", eg_material);
    for (int pt = PAWN; pt < PIECE_TYPE_NB; ++pt) {
        std::vector<int> m = vec(mg, T_PSQT + pt * 64, 64), e = vec(eg, T_PSQT + pt * 64, 64);
        if (pt == PAWN)
            for (int i = 0; i < 64; ++i)
                if (i < 8 || i >= 56) m[i] = e[i] = 0;
        out += board(std::string("MG_") + PIECE_NAMES[pt], m);
        out += board(std::string("EG_") + PIECE_NAMES[pt], e);
    }
    out += scalar("BISHOP_PAIR_MG", mg[T_BISHOP_PAIR]) + scalar("BISHOP_PAIR_EG", eg[T_BISHOP_PAIR]);
    out += scalar("DOUBLED_PAWN_MG", mg[T_DOUBLED]) + scalar("DOUBLED_PAWN_EG", eg[T_DOUBLED]);
    out += scalar("ISOLATED_PAWN_MG", mg[T_ISOLATED]) + scalar("ISOLATED_PAWN_EG", eg[T_ISOLATED]);
    out += scalar("BACKWARD_PAWN_MG", mg[T_BACKWARD]) + scalar("BACKWARD_PAWN_EG", eg[T_BACKWARD]);
    out += array_line("const int CONNECTED_MG[8]", vec(mg, T_CONNECTED, 8));
    out += array_line("const int CONNECTED_EG[8]", vec(eg, T_CONNECTED, 8));
    out += scalar("SUPPORTED_EG", eg[T_SUPPORTED]);
    out += array_line("const int PASSED_PAWN_MG[8]", vec(mg, T_PASSED, 8));
    out += array_line("const int PASSED_PAWN_EG[8]", vec(eg, T_PASSED, 8));
    out += array_line("const int PASSED_FREE_EG[8]", vec(eg, T_PASSED_FREE, 8));
    out += array_line("const int PASSED_SAFE_EG[8]", vec(eg, T_PASSED_SAFE, 8));
    out += array_line("const int PASSED_BLOCKED_EG[8]", vec(eg, T_PASSED_BLOCKED, 8));
    out += array_line("const int PASSED_KING_WEIGHT[8]", vec(eg, T_PASSED_KING, 8));
    out += scalar("ROOK_BEHIND_PASSER_EG", eg[T_ROOK_BEHIND_PASSER]);
    out += scalar("PASSED_UNSTOPPABLE_EG", eg[T_PASSED_UNSTOPPABLE]);
    out += array_line("const int MOBILITY_MG[PIECE_TYPE_NB]", vec(mg, T_MOBILITY, PIECE_TYPE_NB));
    out += array_line("const int MOBILITY_EG[PIECE_TYPE_NB]", vec(eg, T_MOBILITY, PIECE_TYPE_NB));
    out += scalar("MINOR_BEHIND_PAWN_MG", mg[T_MINOR_BEHIND_PAWN]) + scalar("MINOR_BEHIND_PAWN_EG", eg[T_MINOR_BEHIND_PAWN]);
    out += array_line("const int KING_PROTECTOR_MG[PIECE_TYPE_NB]", vec(mg, T_KING_PROTECTOR, PIECE_TYPE_NB));
    out += array_line("const int KING_PROTECTOR_EG[PIECE_TYPE_NB]", vec(eg, T_KING_PROTECTOR, PIECE_TYPE_NB));
    out += scalar("BISHOP_PAWNS_MG", mg[T_BISHOP_PAWNS]) + scalar("BISHOP_PAWNS_EG", eg[T_BISHOP_PAWNS]);
    out += scalar("LONG_DIAGONAL_BISHOP_MG", mg[T_LONG_DIAGONAL]) + scalar("LONG_DIAGONAL_BISHOP_EG", eg[T_LONG_DIAGONAL]);
    out += scalar("TRAPPED_ROOK_MG", mg[T_TRAPPED_ROOK]) + scalar("TRAPPED_ROOK_EG", eg[T_TRAPPED_ROOK]);
    out += array_line("const int SAFE_CHECK_MG[PIECE_TYPE_NB]", vec(mg, T_SAFE_CHECK, PIECE_TYPE_NB));
    out += array_line("const int SAFE_CHECK_EG[PIECE_TYPE_NB]", vec(eg, T_SAFE_CHECK, PIECE_TYPE_NB));
    out += array_line("const int PAWN_STORM[8]", vec(mg, T_PAWN_STORM, 8));
    out += scalar("ROOK_OPEN_FILE_MG", mg[T_ROOK_OPEN]) + scalar("ROOK_OPEN_FILE_EG", eg[T_ROOK_OPEN]);
    out += scalar("ROOK_SEMI_OPEN_MG", mg[T_ROOK_SEMI_OPEN]) + scalar("ROOK_SEMI_OPEN_EG", eg[T_ROOK_SEMI_OPEN]);
    out += scalar("ROOK_ON_SEVENTH_MG", mg[T_ROOK_SEVENTH]) + scalar("ROOK_ON_SEVENTH_EG", eg[T_ROOK_SEVENTH]);
    out += scalar("KNIGHT_OUTPOST_MG", mg[T_KNIGHT_OUTPOST]) + scalar("KNIGHT_OUTPOST_EG", eg[T_KNIGHT_OUTPOST]);
    out += scalar("BISHOP_OUTPOST_MG", mg[T_BISHOP_OUTPOST]) + scalar("BISHOP_OUTPOST_EG", eg[T_BISHOP_OUTPOST]);
    out += array_line("const int THREAT_BY_PAWN_MG[PIECE_TYPE_NB]", vec(mg, T_THREAT_BY_PAWN, PIECE_TYPE_NB));
    out += array_line("const int THREAT_BY_PAWN_EG[PIECE_TYPE_NB]", vec(eg, T_THREAT_BY_PAWN, PIECE_TYPE_NB));
    out += array_line("const int THREAT_BY_MINOR_MG[PIECE_TYPE_NB]", vec(mg, T_THREAT_BY_MINOR, PIECE_TYPE_NB));
    out += array_line("const int THREAT_BY_MINOR_EG[PIECE_TYPE_NB]", vec(eg, T_THREAT_BY_MINOR, PIECE_TYPE_NB));
    out += scalar("THREAT_BY_ROOK_MG", mg[T_THREAT_BY_ROOK]) + scalar("THREAT_BY_ROOK_EG", eg[T_THREAT_BY_ROOK]);
    out += scalar("HANGING_MG", mg[T_HANGING]) + scalar("HANGING_EG", eg[T_HANGING]);
    out += scalar("SHIELD_PAWN_ADVANCED", mg[T_SHIELD_ADVANCED]);
    out += scalar("SHIELD_PAWN_MISSING", mg[T_SHIELD_MISSING]);
    out += scalar("KING_FILE_SEMI_OPEN", mg[T_KING_FILE_SEMI_OPEN]);
    out += scalar("KING_FILE_OPEN", mg[T_KING_FILE_OPEN]);
    return out;
}

} // namespace Tune
#endif

} // namespace Eval
