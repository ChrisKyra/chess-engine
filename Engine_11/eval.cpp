#include "eval.h"
#include "movegen.h"

#include <algorithm>

namespace Eval {

const int PIECE_VALUE[PIECE_TYPE_NB] = { 100, 320, 335, 500, 975, 0 };

namespace {

// ---------------------------------------------------------------------------
// Material.  Two sets of values are kept: one for the middlegame and one for
// the endgame.  Rooks and pawns gain value as the board empties, minor pieces
// roughly hold theirs, so a single number cannot describe both phases.
// ---------------------------------------------------------------------------

const int MG_MATERIAL[PIECE_TYPE_NB] = { 100, 320, 335, 500, 975, 0 };
const int EG_MATERIAL[PIECE_TYPE_NB] = { 120, 330, 350, 545, 1000, 0 };

// ---------------------------------------------------------------------------
// Piece-square tables, written as they appear on the board with rank 8 on the
// first line, from White's point of view.  Index 0 is therefore a8, which is
// why the lookup below flips the square for White and uses it directly for
// Black (a black piece on a3 is the mirror of a white piece on a6).
// ---------------------------------------------------------------------------

const int MG_PAWN[64] = {
      0,   0,   0,   0,   0,   0,   0,   0,
     60,  62,  58,  55,  55,  58,  62,  60,
     18,  22,  32,  40,  40,  32,  22,  18,
      8,  12,  18,  30,  30,  18,  12,   8,
      2,   4,   8,  22,  22,   8,   4,   2,
      4,  -2,  -6,   2,   2,  -8,  -2,   4,
      4,   8,   8, -18, -18,  12,   8,   4,
      0,   0,   0,   0,   0,   0,   0,   0
};

const int EG_PAWN[64] = {
      0,   0,   0,   0,   0,   0,   0,   0,
    120, 118, 112, 105, 105, 112, 118, 120,
     70,  68,  62,  55,  55,  62,  68,  70,
     36,  32,  28,  24,  24,  28,  32,  36,
     18,  16,  12,  10,  10,  12,  16,  18,
      6,   6,   4,   4,   4,   4,   6,   6,
      2,   2,   2,   2,   2,   2,   2,   2,
      0,   0,   0,   0,   0,   0,   0,   0
};

const int MG_KNIGHT[64] = {
    -60, -40, -30, -28, -28, -30, -40, -60,
    -38, -18,   2,   6,   6,   2, -18, -38,
    -28,   4,  20,  26,  26,  20,   4, -28,
    -24,   8,  26,  32,  32,  26,   8, -24,
    -24,   6,  24,  32,  32,  24,   6, -24,
    -28,   6,  18,  24,  24,  20,   6, -28,
    -38, -18,   2,   8,   8,   2, -18, -38,
    -62, -32, -26, -22, -22, -26, -32, -62
};

const int EG_KNIGHT[64] = {
    -50, -34, -22, -18, -18, -22, -34, -50,
    -32, -14,   0,   6,   6,   0, -14, -32,
    -22,   2,  14,  20,  20,  14,   2, -22,
    -18,   6,  20,  26,  26,  20,   6, -18,
    -18,   6,  20,  26,  26,  20,   6, -18,
    -22,   2,  14,  20,  20,  14,   2, -22,
    -32, -14,   0,   6,   6,   0, -14, -32,
    -50, -34, -22, -18, -18, -22, -34, -50
};

const int MG_BISHOP[64] = {
    -22, -12, -14, -10, -10, -14, -12, -22,
    -12,   4,   0,   2,   2,   0,   4, -12,
     -6,   6,  10,  10,  10,  10,   6,  -6,
     -4,   4,  12,  18,  18,  12,   4,  -4,
     -4,   8,  12,  18,  18,  12,   8,  -4,
     -6,  14,  12,  12,  12,  12,  14,  -6,
    -12,  14,   6,   4,   4,   6,  14, -12,
    -24,  -8, -12, -14, -14, -12,  -8, -24
};

const int EG_BISHOP[64] = {
    -16, -10,  -8,  -4,  -4,  -8, -10, -16,
    -10,  -2,   2,   2,   2,   2,  -2, -10,
     -4,   4,   6,   8,   8,   6,   4,  -4,
     -2,   4,  10,  12,  12,  10,   4,  -2,
     -2,   4,  10,  12,  12,  10,   4,  -2,
     -4,   4,   6,   8,   8,   6,   4,  -4,
    -10,  -2,   2,   2,   2,   2,  -2, -10,
    -16, -10,  -8,  -4,  -4,  -8, -10, -16
};

const int MG_ROOK[64] = {
      0,   2,   4,   6,   6,   4,   2,   0,
     10,  14,  14,  16,  16,  14,  14,  10,
     -4,   0,   2,   4,   4,   2,   0,  -4,
     -6,  -2,   0,   2,   2,   0,  -2,  -6,
     -6,  -2,   0,   2,   2,   0,  -2,  -6,
     -6,  -2,   0,   2,   2,   0,  -2,  -6,
     -8,  -2,   0,   2,   2,   0,  -2,  -8,
     -4,  -2,   4,  10,  10,   6,  -2,  -4
};

const int EG_ROOK[64] = {
      8,   8,   8,   8,   8,   8,   8,   8,
     10,  10,  10,  10,  10,  10,  10,  10,
      4,   4,   4,   4,   4,   4,   4,   4,
      2,   2,   2,   2,   2,   2,   2,   2,
      0,   0,   0,   0,   0,   0,   0,   0,
     -2,  -2,  -2,  -2,  -2,  -2,  -2,  -2,
     -4,  -4,  -4,  -4,  -4,  -4,  -4,  -4,
     -2,  -2,  -2,  -2,  -2,  -2,  -2,  -2
};

const int MG_QUEEN[64] = {
    -18, -10,  -8,  -4,  -4,  -8, -10, -18,
    -10,   0,   4,   0,   0,   4,   0, -10,
     -8,   4,   6,   6,   6,   6,   4,  -8,
     -4,   0,   6,   8,   8,   6,   0,  -4,
     -2,   2,   6,   8,   8,   6,   2,  -2,
     -8,   6,   6,   6,   6,   6,   6,  -8,
    -10,   2,   6,   2,   2,   4,   0, -10,
    -18, -10,  -8,  -2,  -4,  -8, -10, -18
};

const int EG_QUEEN[64] = {
    -16, -10,  -6,  -2,  -2,  -6, -10, -16,
    -10,   0,   4,   8,   8,   4,   0, -10,
     -6,   4,  10,  14,  14,  10,   4,  -6,
     -2,   8,  14,  18,  18,  14,   8,  -2,
     -2,   8,  14,  18,  18,  14,   8,  -2,
     -6,   4,  10,  14,  14,  10,   4,  -6,
    -10,   0,   4,   8,   8,   4,   0, -10,
    -16, -10,  -6,  -2,  -2,  -6, -10, -16
};

// In the middlegame the king belongs tucked behind its pawns; in the endgame it
// becomes a fighting piece and wants the centre.  The two tables disagree
// sharply, which is exactly what the phase interpolation is for.
const int MG_KING[64] = {
    -40, -50, -50, -60, -60, -50, -50, -40,
    -40, -50, -50, -60, -60, -50, -50, -40,
    -40, -50, -50, -60, -60, -50, -50, -40,
    -40, -50, -50, -60, -60, -50, -50, -40,
    -30, -40, -40, -50, -50, -40, -40, -30,
    -20, -30, -30, -40, -40, -30, -30, -20,
     14,  14,  -6, -20, -20,  -8,  14,  14,
     18,  30,   8, -12,   4, -14,  32,  20
};

const int EG_KING[64] = {
    -58, -34, -20, -12, -12, -20, -34, -58,
    -24,  -8,  10,  18,  18,  10,  -8, -24,
    -14,  12,  30,  38,  38,  30,  12, -14,
    -14,  16,  38,  46,  46,  38,  16, -14,
    -16,  14,  36,  44,  44,  36,  14, -16,
    -20,   6,  24,  30,  30,  24,   6, -20,
    -30, -12,   4,  10,  10,   4, -12, -30,
    -58, -38, -26, -20, -20, -26, -38, -58
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

constexpr int BISHOP_PAIR_MG = 24;
constexpr int BISHOP_PAIR_EG = 46;
constexpr int TEMPO = 12;

// ---- pawn structure ----
constexpr int DOUBLED_PAWN_MG = -8;
constexpr int DOUBLED_PAWN_EG = -22;
constexpr int ISOLATED_PAWN_MG = -14;
constexpr int ISOLATED_PAWN_EG = -18;
constexpr int BACKWARD_PAWN_MG = -10;
constexpr int BACKWARD_PAWN_EG = -14;

// Pawns defending each other or standing side by side, by relative rank.
const int CONNECTED_MG[8] = { 0, 4, 6, 10, 18, 34, 60, 0 };
const int CONNECTED_EG[8] = { 0, 2, 4, 8, 14, 28, 50, 0 };
constexpr int SUPPORTED_EG = 6;             // per pawn defending this one

// ---- passed pawns ----
const int PASSED_PAWN_MG[8] = { 0, 4, 8, 16, 34, 62, 96, 0 };
const int PASSED_PAWN_EG[8] = { 0, 10, 18, 34, 62, 110, 172, 0 };
const int PASSED_FREE_EG[8] = { 0, 0, 4, 10, 22, 44, 70, 0 };     // no piece in the way
const int PASSED_SAFE_EG[8] = { 0, 0, 2, 6, 14, 28, 44, 0 };      // and no enemy attack on the way
const int PASSED_BLOCKED_EG[8] = { 0, 0, -2, -4, -10, -18, -28, 0 };  // an enemy piece sits in front
const int PASSED_KING_WEIGHT[8] = { 0, 0, 0, 4, 8, 14, 22, 0 };   // per square of king distance
constexpr int ROOK_BEHIND_PASSER_EG = 22;
constexpr int PASSED_UNSTOPPABLE_EG = 450;  // the enemy king cannot catch it at all

// ---- pieces ----
// Mobility is counted in "safe squares this piece can reach", minus a baseline
// so that an average piece scores zero and only unusually free or unusually
// cramped pieces move the evaluation.
const int MOBILITY_MG[PIECE_TYPE_NB] = { 0, 4, 4, 2, 1, 0 };
const int MOBILITY_EG[PIECE_TYPE_NB] = { 0, 4, 5, 4, 2, 0 };
const int MOBILITY_BASE[PIECE_TYPE_NB] = { 0, 4, 6, 7, 13, 0 };

constexpr int ROOK_OPEN_FILE_MG = 26;
constexpr int ROOK_OPEN_FILE_EG = 12;
constexpr int ROOK_SEMI_OPEN_MG = 12;
constexpr int ROOK_SEMI_OPEN_EG = 6;
constexpr int ROOK_ON_SEVENTH_MG = 16;
constexpr int ROOK_ON_SEVENTH_EG = 32;
constexpr int KNIGHT_OUTPOST_MG = 28;
constexpr int KNIGHT_OUTPOST_EG = 16;
constexpr int BISHOP_OUTPOST_MG = 18;
constexpr int BISHOP_OUTPOST_EG = 8;

// ---- threats, by the type of piece being threatened ----
const int THREAT_BY_PAWN_MG[PIECE_TYPE_NB] = { 0, 48, 48, 72, 84, 0 };
const int THREAT_BY_PAWN_EG[PIECE_TYPE_NB] = { 0, 40, 40, 60, 70, 0 };
const int THREAT_BY_MINOR_MG[PIECE_TYPE_NB] = { 0, 0, 0, 36, 50, 0 };
const int THREAT_BY_MINOR_EG[PIECE_TYPE_NB] = { 0, 0, 0, 24, 32, 0 };
constexpr int THREAT_BY_ROOK_MG = 32;       // a rook attacking the queen
constexpr int THREAT_BY_ROOK_EG = 20;
constexpr int HANGING_MG = 20;              // per piece attacked and undefended
constexpr int HANGING_EG = 24;

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
constexpr int SHIELD_PAWN_ADVANCED = -8;    // the shield pawn has moved two squares up
constexpr int SHIELD_PAWN_MISSING = -20;    // no shield pawn within two squares
constexpr int KING_FILE_SEMI_OPEN = -10;    // none of our pawns on the file
constexpr int KING_FILE_OPEN = -15;         // no pawns of either colour on the file

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

        if (doubled) score.add(DOUBLED_PAWN_MG, DOUBLED_PAWN_EG);

        if (!neighbours) score.add(ISOLATED_PAWN_MG, ISOLATED_PAWN_EG);
        else if (backward) score.add(BACKWARD_PAWN_MG, BACKWARD_PAWN_EG);

        if (support || phalanx) {
            int mg = CONNECTED_MG[r];
            int eg = CONNECTED_EG[r];
            // Standing side by side is worth more than being defended from
            // behind: the pair controls squares and advances together.
            if (phalanx) { mg += mg / 2; eg += eg / 2; }
            eg += SUPPORTED_EG * popcount(support);
            score.add(mg, eg);
        }

        if (passed) {
            e.passed[Us] |= square_bb(s);
            score.add(PASSED_PAWN_MG[r], PASSED_PAWN_EG[r]);
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
    if (e.pawns[WHITE] == white && e.pawns[BLACK] == black) return &e;

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

            const int moves = popcount(attacks & mobility_area[Us]) - MOBILITY_BASE[pt];
            score.add(moves * MOBILITY_MG[pt], moves * MOBILITY_EG[pt]);

            if (const U64 zone_hits = attacks & king_zone[Them]) {
                ++king_attackers[Us];
                king_attack_units[Us] += KING_ATTACK_WEIGHT[pt] * popcount(zone_hits);
            }

            if (pt == KNIGHT && is_outpost<Us>(s))
                score.add(KNIGHT_OUTPOST_MG, KNIGHT_OUTPOST_EG);
            else if (pt == BISHOP && is_outpost<Us>(s))
                score.add(BISHOP_OUTPOST_MG, BISHOP_OUTPOST_EG);

            if (pt == ROOK) {
                const U64 file = FILE_BB[file_of(s)];
                if (!(file & own_pawns))
                    score.add((file & enemy_pawns) ? ROOK_SEMI_OPEN_MG : ROOK_OPEN_FILE_MG,
                              (file & enemy_pawns) ? ROOK_SEMI_OPEN_EG : ROOK_OPEN_FILE_EG);

                if (relative_rank(Us, s) == 6
                    && (relative_rank(Us, pos.king_square(Them)) == 7
                        || (enemy_pawns & RANK_BB[rank_of(s)])))
                    score.add(ROOK_ON_SEVENTH_MG, ROOK_ON_SEVENTH_EG);
            }
        }
    }

    // Two bishops cover both square colours between them, which is worth more
    // than the sum of the pieces, and worth more still as the board empties.
    if (popcount(pos.pieces(Us, BISHOP)) >= 2)
        score.add(BISHOP_PAIR_MG, BISHOP_PAIR_EG);

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

        if (!(own_pawns & FILE_BB[f]))
            mg += (enemy_pawns & FILE_BB[f]) ? KING_FILE_SEMI_OPEN : KING_FILE_OPEN;
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
    }

    threatened = (attacked_by[Us][KNIGHT] | attacked_by[Us][BISHOP])
               & (pos.pieces(Them, ROOK) | pos.pieces(Them, QUEEN));
    while (threatened) {
        const PieceType victim = type_of(pos.piece_on(pop_lsb(threatened)));
        score.add(THREAT_BY_MINOR_MG[victim], THREAT_BY_MINOR_EG[victim]);
    }

    if (attacked_by[Us][ROOK] & pos.pieces(Them, QUEEN))
        score.add(THREAT_BY_ROOK_MG, THREAT_BY_ROOK_EG);

    const int hanging = popcount(their_pieces & attacked_by[Us][ALL_PIECES]
                                 & ~attacked_by[Them][ALL_PIECES]);
    score.add(HANGING_MG * hanging, HANGING_EG * hanging);

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
        }

        if (!(path & occupied)) {
            eg += PASSED_FREE_EG[r];
            if (!(path & attacked_by[Them][ALL_PIECES])) eg += PASSED_SAFE_EG[r];
        } else if (pos.pieces(Them) & square_bb(stop)) {
            eg += PASSED_BLOCKED_EG[r];
        }

        // Squares behind the pawn on its own file, as far as a rook standing
        // there could actually see.
        const U64 behind = FORWARD_FILE[Them][s] & rook_attacks(s, occupied);
        if (behind & pos.pieces(Us, ROOK)) eg += ROOK_BEHIND_PASSER_EG;
        else if (behind & pos.pieces(Them, ROOK)) eg -= ROOK_BEHIND_PASSER_EG;

        if (!non_pawn_material(Them) && !(path & occupied)) {
            const Square promotion = make_square(file_of(s), (Us == WHITE) ? 7 : 0);
            // A pawn still on its starting rank saves a move with the double push.
            const int pawn_steps = 7 - r - (r == 1 ? 1 : 0);
            // Moving first is worth a move of head start to the defender.
            const int king_steps = DISTANCE[their_king][promotion]
                                 - (pos.side_to_move() == Them ? 1 : 0);
            if (king_steps > pawn_steps) eg += PASSED_UNSTOPPABLE_EG;
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

    return scale;
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

    const int eg = score.eg * scale_factor(score.eg) / SCALE_NORMAL;
    const int blended = (score.mg * phase + eg * (MAX_PHASE - phase)) / MAX_PHASE;

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

} // namespace Eval
