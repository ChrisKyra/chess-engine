#include "search.h"
#include "eval.h"
#include "movegen.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <vector>

namespace Search {

namespace {

// ---------------------------------------------------------------------------
// Transposition table.  The same position is reached by many move orders, so
// caching the result of searching it is the single biggest saving available.
// Each entry records how deep the stored score was searched, whether it is
// exact, a lower bound (the search failed high) or an upper bound (failed low),
// the best move, and the static evaluation.
//
// The table is split into buckets of four entries.  A position may sit in any
// entry of its bucket, so a new result rarely has to evict a valuable one.
// ---------------------------------------------------------------------------

enum Bound : uint8_t { BOUND_NONE, BOUND_UPPER, BOUND_LOWER, BOUND_EXACT };

// What an entry says about a position, unpacked.
struct TTData {
    int score = 0;
    int eval = 0;
    Move move = MOVE_NONE;
    int depth = -1;           // -1: empty; 0: stored by quiescence search
    uint8_t bound = BOUND_NONE;
    uint8_t age = 0;
};

// Only six bits are left for the age, so "how many searches ago" wraps at 64.
constexpr int TT_AGE_MASK = 63;

// All of that squeezed into one 64-bit word: move 16, score 16, eval 16,
// depth 8, bound 2, age 6.  Packing matters because a word can be written in
// one machine instruction, which is what makes the entry safe to share between
// threads without a lock.
U64 pack(const TTData& d) {
    return U64(d.move.raw())
         | (U64(uint16_t(int16_t(d.score))) << 16)
         | (U64(uint16_t(int16_t(d.eval))) << 32)
         | (U64(uint8_t(d.depth + 1)) << 48)
         | (U64(d.bound & 3) << 56)
         | (U64(d.age & TT_AGE_MASK) << 58);
}

TTData unpack(U64 word) {
    TTData d;
    d.move = Move(uint16_t(word & 0xFFFF));
    d.score = int16_t((word >> 16) & 0xFFFF);
    d.eval = int16_t((word >> 32) & 0xFFFF);
    d.depth = int(uint8_t((word >> 48) & 0xFF)) - 1;
    d.bound = uint8_t((word >> 56) & 3);
    d.age = uint8_t((word >> 58) & TT_AGE_MASK);
    return d;
}

// One entry: the packed payload, and the position key with that payload xored
// into it.
//
// Several threads write the same bucket at once.  Each word is written in one
// piece, but the pair is not, so a reader can catch the key of one store next
// to the payload of another.  Xoring them together catches exactly that: the
// key only comes back out when both words are from the same store.  A mismatch
// looks like a miss, which costs a re-search and never a wrong answer.
struct TTEntry {
    std::atomic<U64> key{0};
    std::atomic<U64> data{0};
};

constexpr int BUCKET_SIZE = 4;

// 64 bytes: a whole bucket arrives in one cache line, and no bucket shares a
// line with another, so threads working on different buckets do not fight over
// the same line.
struct alignas(64) Bucket {
    TTEntry entries[BUCKET_SIZE];
};

std::vector<Bucket> tt;
size_t tt_mask = 0;
uint8_t tt_age = 0;

// Reads one entry.  `stored_key` is the position it belongs to, and the result
// is false when the entry is empty (or was caught half-written).
bool tt_read(const TTEntry& e, U64& stored_key, TTData& data) {
    const U64 key_word = e.key.load(std::memory_order_relaxed);
    const U64 data_word = e.data.load(std::memory_order_relaxed);
    stored_key = key_word ^ data_word;
    data = unpack(data_word);
    return data.depth >= 0;
}

// How willing we are to overwrite an entry: lower goes first.  Entries left by
// earlier searches lose 8 plies of "depth" per search they are old, so stale
// results make way before deep, current ones.
int replacement_value(const TTData& d) {
    return d.depth - 8 * ((64 + tt_age - d.age) & TT_AGE_MASK);
}

// Finds `key` in its bucket.  On a hit the return value points at the entry
// holding it and `data` is its contents; on a miss it points at the entry a new
// result should replace, and `data` is whatever is there now.
TTEntry* tt_probe(U64 key, bool& hit, TTData& data) {
    Bucket& bucket = tt[key & tt_mask];

    TTEntry* victim = &bucket.entries[0];
    TTData victim_data;
    bool have_victim = false;

    for (TTEntry& e : bucket.entries) {
        U64 stored_key = 0;
        TTData current;
        const bool occupied = tt_read(e, stored_key, current);
        if (occupied && stored_key == key) {
            hit = true;
            data = current;
            return &e;
        }
        if (!have_victim || replacement_value(current) < replacement_value(victim_data)) {
            victim = &e;
            victim_data = current;
            have_victim = true;
        }
    }

    hit = false;
    data = victim_data;
    return victim;
}

// Writes a result.  `old` is what tt_probe() found in this entry and
// `same_position` whether that was a hit for this key.
void tt_store(TTEntry* e, const TTData& old, bool same_position,
              U64 key, int score, int eval, Move move, int depth, uint8_t bound) {
    // Writes both words: the payload first, then the key that validates it.
    auto write = [e, key](const TTData& d) {
        const U64 word = pack(d);
        e->data.store(word, std::memory_order_relaxed);
        e->key.store(key ^ word, std::memory_order_relaxed);
    };

    // Keep a known best move when this search didn't find one.
    const Move best = (!move.is_none() || !same_position) ? move : old.move;

    // A clearly shallower, inexact result from this same search doesn't
    // overwrite a deeper one for the same position -- but the move it found is
    // still worth recording, since ordering is what the move is used for.
    if (same_position && old.age == (tt_age & TT_AGE_MASK)
        && depth < old.depth - 2 && bound != BOUND_EXACT) {
        if (best != old.move) {
            TTData kept = old;
            kept.move = best;
            write(kept);
        }
        return;
    }

    TTData d;
    d.score = score;
    d.eval = eval;
    d.move = best;
    d.depth = depth;
    d.bound = bound;
    d.age = tt_age;
    write(d);
}

// Mate scores are stored relative to the entry's own position, not the root,
// otherwise a cached "mate in 3" would be wrong when reached at another depth.
int score_to_tt(int score, int ply) {
    if (score >= VALUE_MATE_IN_MAX_PLY) return score + ply;
    if (score <= -VALUE_MATE_IN_MAX_PLY) return score - ply;
    return score;
}

int score_from_tt(int score, int ply) {
    if (score >= VALUE_MATE_IN_MAX_PLY) return score - ply;
    if (score <= -VALUE_MATE_IN_MAX_PLY) return score + ply;
    return score;
}

bool tt_cutoff(const TTData& d, int score, int alpha, int beta) {
    return d.bound == BOUND_EXACT
        || (d.bound == BOUND_LOWER && score >= beta)
        || (d.bound == BOUND_UPPER && score <= alpha);
}

// ---------------------------------------------------------------------------
// Pruning and reduction parameters
// ---------------------------------------------------------------------------

// Futility pruning: at depth 1-3, if the static evaluation plus this margin
// cannot reach alpha, quiet moves are not expected to help.
constexpr int FUTILITY_MARGIN[4] = { 0, 120, 220, 320 };

// Late-move pruning: at depth 1-3, quiet moves beyond this many are skipped.
constexpr int LATE_MOVE_LIMIT[4] = { 0, 6, 10, 16 };

// Static exchange pruning in the main search: up to this depth, captures losing
// more than SEE_CAPTURE_MARGIN per ply and quiet moves losing more than
// SEE_QUIET_MARGIN times depth squared are skipped.
constexpr int SEE_PRUNE_DEPTH = 8;

// Time management on a clock: the soft limit is multiplied by STABILITY_SCALE,
// indexed by how many iterations in a row ended on the same best move (4 or
// more share the last entry), and by SCORE_DROP_SCALE when the score fell more
// than SCORE_DROP_MARGIN centipawns since the previous iteration.
constexpr double STABILITY_SCALE[5] = { 1.5, 1.2, 1.0, 0.85, 0.75 };
constexpr int SCORE_DROP_MARGIN = 30;
constexpr double SCORE_DROP_SCALE = 1.3;

// Singular extensions are tried from this depth up.
constexpr int SINGULAR_DEPTH = 8;
constexpr int SEE_CAPTURE_MARGIN = 100;
constexpr int SEE_QUIET_MARGIN = 40;

// History scores are kept within +-HISTORY_MAX (see update_history).
constexpr int HISTORY_MAX = 16384;

// Late move reductions by [depth][move number]: grows with the logarithm of
// both, so deep searches reduce late moves more, but never explosively.
int REDUCTIONS[64][64];

struct ReductionsInit {
    ReductionsInit() {
        for (int d = 0; d < 64; ++d)
            for (int m = 0; m < 64; ++m)
                REDUCTIONS[d][m] = (d == 0 || m == 0)
                    ? 0
                    : int(0.75 + std::log(double(d)) * std::log(double(m)) / 2.25);
    }
} reductions_init;

// ---------------------------------------------------------------------------
// Search state
// ---------------------------------------------------------------------------

// Set by the UCI thread ("stop", "quit", end of input), read by the search.
std::atomic<bool> stop_flag{false};

// True from the moment a search starts until the main thread is done with it.
// The helper threads poll it: clearing it is how they are told to stop, without
// touching the stop flag, which means something else to the UCI loop.
std::atomic<bool> searching{false};

// How many threads search at once (the "Threads" option).
int thread_count = 1;

// Helper threads skip depths on these patterns, so that at any moment they are
// spread over several depths instead of all repeating the same one.  Thread i
// uses entry (i - 1) % 20.
constexpr int SKIP_SIZE[20]  = { 1, 1, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4 };
constexpr int SKIP_PHASE[20] = { 0, 1, 0, 1, 2, 0, 1, 2, 3, 4, 5, 6, 7, 8, 0, 1, 2, 3, 4, 5 };

// Set while "go ponder" thinks on the opponent's time: no time limit applies
// until "ponderhit" clears it.
std::atomic<bool> ponder_flag{false};

// When the clock for the time limits started, in milliseconds.  "ponderhit"
// restarts it, so the move gets its whole budget from that moment.
std::atomic<int64_t> clock_start_ms{0};

std::mutex output_mutex;

// Time kept back from every move for reading the answer and sending it
// ("Move Overhead").  Only changed between searches.
int64_t move_overhead_ms = 10;

// How many centipawns a draw is worth against us ("Contempt").  Only changed
// between searches.
int contempt_cp = 25;

// After this long, the search also reports the root move it is working on, and
// scores that fell outside the aspiration window.
constexpr int64_t CURRMOVE_DELAY_MS = 3000;
constexpr int64_t BOUND_REPORT_DELAY_MS = 1000;

int64_t now_ms() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
               std::chrono::steady_clock::now().time_since_epoch()).count();
}

struct Stack {
    Move killers[2] = { MOVE_NONE, MOVE_NONE };
    Move move = MOVE_NONE;       // the move being searched from this ply (none for a null move)
    Piece piece = NO_PIECE;      // the piece that made it
    Move excluded = MOVE_NONE;     // set while searching this node without one move (singular extension)
};

// Everything one searching thread needs.  Nothing in here is shared: each
// thread has its own history, killers, counter moves and copy of the position,
// which is what makes the threads explore the position differently.  The only
// things they share are the transposition table and the flags above.
struct Searcher {
    int id = 0;               // 0 is the main thread, whose move is the one played
    uint64_t nodes = 0;
    uint64_t node_limit = 0;  // "go nodes", 0 for none
    int seldepth = 0;         // deepest ply reached, for "info seldepth"
    int root_depth = 0;       // the iteration being searched
    bool stopped = false;
    bool can_stop = false;    // false until depth 1 is complete

    // This thread's node count, republished every 2048 nodes so the other
    // threads can total them up without reading a counter that is mid-update.
    std::atomic<uint64_t> published_nodes{0};

    // What the other threads had published when we last looked.
    uint64_t others_nodes = 0;

    // This thread's own copy of the root position, and its answer.
    Position root;
    Result result;

    // Who is to move at the root.  Contempt is from this side's point of view:
    // every thread of one search shares it, so they all agree which side would
    // rather not draw.
    Color root_side = WHITE;

    // The contempt this search uses: the "Contempt" option when playing, zero
    // when analysing ("go infinite"), where a draw should be shown as level.
    int contempt = 0;

    // "go searchmoves": the only root moves considered (empty: all of them).
    std::vector<Move> searchmoves;

    int64_t soft_limit = 0;   // do not start a new iteration past this (before scaling, see below)
    int64_t hard_limit = 0;   // abort immediately past this

    // Playing on a clock, the soft limit is scaled by how settled the search
    // is (see iterate); with a fixed time per move it is used as it is.
    bool scale_soft_limit = false;
    std::chrono::steady_clock::time_point start;

    // Milliseconds on the clock that the time limits are measured against.
    int64_t time_used() const {
        return now_ms() - clock_start_ms.load(std::memory_order_relaxed);
    }

    Stack stack[MAX_PLY + 4];
    int history[COLOR_NB][SQUARE_NB][SQUARE_NB] = {};

    // Continuation history: how well a quiet move (piece, destination) has done
    // as the reply to an earlier move (piece, destination).  The same table is
    // read with the opponent's last move and with our own move before that, so
    // it learns follow-ups ("after ...Nf6, h3 works") that the plain history,
    // which ignores what came before, cannot tell apart.
    int16_t continuation[PIECE_NB][SQUARE_NB][PIECE_NB][SQUARE_NB] = {};

    // Capture history: how well a capture (piece, destination, captured type)
    // has done, used to order captures of equal victim and attacker.
    int16_t capture_history[PIECE_NB][SQUARE_NB][PIECE_TYPE_NB] = {};

    // The quiet move that last refuted a move, indexed by that move's piece
    // and destination square.
    Move counter_moves[PIECE_NB][SQUARE_NB];

    Move pv[MAX_PLY][MAX_PLY];
    int pv_length[MAX_PLY] = {};

    int64_t elapsed() const {
        return std::chrono::duration_cast<std::chrono::milliseconds>(
                   std::chrono::steady_clock::now() - start).count();
    }

    // Gets this thread ready for a new search.  The history tables are kept and
    // halved rather than cleared: what was learned about the previous move is
    // still broadly useful, just one move less trustworthy.
    void prepare(const Position& position, const Limits& limits,
                 std::chrono::steady_clock::time_point begin) {
        root = position;
        root_side = position.side_to_move();
        contempt = limits.infinite ? 0 : contempt_cp;
        result = Result();
        nodes = 0;
        published_nodes.store(0, std::memory_order_relaxed);
        others_nodes = 0;
        node_limit = limits.nodes;
        seldepth = 0;
        root_depth = 0;
        stopped = false;
        can_stop = false;
        start = begin;

        for (int i = 0; i < MAX_PLY + 4; ++i) stack[i] = Stack();

        for (int c = 0; c < COLOR_NB; ++c)
            for (int a = 0; a < SQUARE_NB; ++a)
                for (int b = 0; b < SQUARE_NB; ++b)
                    history[c][a][b] /= 2;
    }

    // Forgets everything learned, for "ucinewgame".
    void clear_heuristics() {
        std::memset(history, 0, sizeof(history));
        std::memset(continuation, 0, sizeof(continuation));
        std::memset(capture_history, 0, sizeof(capture_history));
        for (auto& by_square : counter_moves)
            std::fill(std::begin(by_square), std::end(by_square), MOVE_NONE);
        for (int i = 0; i < MAX_PLY + 4; ++i) stack[i] = Stack();
    }
};

// One per thread, kept between searches so the history tables survive.
std::vector<std::unique_ptr<Searcher>> searchers;

// What every thread has searched between them, for the "nodes" report and the
// node limit.
uint64_t total_nodes() {
    uint64_t total = 0;
    for (const auto& searcher : searchers)
        total += searcher->published_nodes.load(std::memory_order_relaxed);
    return total;
}

// What a draw is worth at this node.
//
// Not zero: a draw counts as `s.contempt` against whichever side started the
// search, and in its opponent's favour.  Without this, a repetition scores
// exactly level, which makes it the safest move available in any position the
// engine does not think it is winning -- so two equal engines repeat and draw.
// With it, the engine only takes the draw when its alternatives are worse than
// giving up that much.
//
// The sign follows the side to move at the node, because negamax negates the
// score at every ply; both sides then agree about who dislikes the draw.
int draw_value(const Searcher& s, Color side_to_move) {
    return side_to_move == s.root_side ? -s.contempt : s.contempt;
}

// Moves the history score towards +-HISTORY_MAX: the closer it already is, the
// less a bonus adds, so scores stay bounded and old information fades.
void update_history(int& score, int bonus) {
    score += bonus - score * std::abs(bonus) / HISTORY_MAX;
}

template<typename T>
void update_history(T& score, int bonus) {
    int value = score;
    update_history(value, bonus);
    score = T(value);
}

// The continuation history entry for playing `piece` to `to` after the move
// made `back` plies earlier, or nullptr when there was none (a null move, or
// before the root).
int16_t* continuation_entry(Searcher& s, int ply, int back, Piece piece, Square to) {
    if (ply < back) return nullptr;
    const Stack& earlier = s.stack[ply - back];
    if (earlier.move.is_none()) return nullptr;
    return &s.continuation[earlier.piece][earlier.move.to()][piece][to];
}

// Rewards (or, with a negative bonus, penalises) a quiet move in the plain
// history and in the continuation history after the last two moves.
void update_quiet_histories(Searcher& s, int ply, Color us, Move m, Piece piece, int bonus) {
    update_history(s.history[us][m.from()][m.to()], bonus);
    for (int back : { 1, 2 })
        if (int16_t* entry = continuation_entry(s, ply, back, piece, m.to())) update_history(*entry, bonus);
}

// What the continuation history says about a quiet move: the sum over the
// opponent's last move and our own move before it.
int continuation_score(Searcher& s, int ply, Piece piece, Square to) {
    int total = 0;
    for (int back : { 1, 2 })
        if (const int16_t* entry = continuation_entry(s, ply, back, piece, to)) total += *entry;
    return total;
}

// The piece type a capture takes (a pawn for en passant).
PieceType captured_type(const Position& pos, Move m) {
    return m.is_en_passant() ? PAWN : type_of(pos.piece_on(m.to()));
}

// The counter move to the opponent's previous move, if any.
Move counter_move(Searcher& s, int ply) {
    if (ply == 0) return MOVE_NONE;
    const Stack& prev = s.stack[ply - 1];
    return prev.move.is_none() ? MOVE_NONE : s.counter_moves[prev.piece][prev.move.to()];
}

// Most Valuable Victim / Least Valuable Attacker: PxQ is searched before QxP,
// because the former is far more likely to be good and an early cutoff prunes
// more of the tree.
int mvv_lva(const Position& pos, Move m) {
    PieceType victim = m.is_en_passant() ? PAWN : type_of(pos.piece_on(m.to()));
    PieceType attacker = type_of(pos.piece_on(m.from()));
    return Eval::PIECE_VALUE[victim] * 16 - Eval::PIECE_VALUE[attacker];
}

// Ordering is where alpha-beta lives or dies: the sooner the best move is
// tried, the more of the remaining tree can be cut.
void score_moves(Searcher& s, const Position& pos, MoveList& list, Move tt_move, int ply) {
    const Color us = pos.side_to_move();
    const Move counter = counter_move(s, ply);
    for (ScoredMove& sm : list) {
        Move m = sm.move;
        if (m == tt_move) {
            sm.score = 2000000;
        } else if (m.is_promotion()) {
            sm.score = 1500000 + Eval::PIECE_VALUE[m.promotion_type()];
        } else if (m.is_capture()) {
            // Captures that win or break even once the exchange is played out
            // come straight after promotions; captures that lose material go to
            // the back, behind the quiet moves.  Quiescence relies on the
            // negative score to skip them.
            // Capture history breaks ties between captures of equal victims.
            const Piece piece = pos.piece_on(m.from());
            sm.score = (pos.see(m) >= 0 ? 1000000 : -1000000) + mvv_lva(pos, m)
                     + s.capture_history[piece][m.to()][captured_type(pos, m)] / 16;
        } else if (m == s.stack[ply].killers[0]) {
            sm.score = 900000;
        } else if (m == s.stack[ply].killers[1]) {
            sm.score = 800000;
        } else if (m == counter) {
            sm.score = 700000;
        } else {
            sm.score = s.history[us][m.from()][m.to()]
                     + continuation_score(s, ply, pos.piece_on(m.from()), m.to());
        }
    }
}

// Selection sort one move at a time: with an early cutoff most of the list is
// never examined, so sorting it up front would be wasted work.
Move pick_move(MoveList& list, int index) {
    int best = index;
    for (int i = index + 1; i < list.size(); ++i)
        if (list[i].score > list[best].score) best = i;
    std::swap(list[index], list[best]);
    return list[index].move;
}

// Stop once the node limit is reached, and, every 2048 nodes, on request, when
// the main thread is done, or when the hard time limit passes (not while
// pondering) -- but never before depth 1 has produced a move.
//
// Every 2048 nodes is also when this thread publishes its node count and picks
// up the other threads', so that the node limit counts what all of them have
// searched rather than only this one.
void check_time(Searcher& s) {
    if ((s.nodes & 2047) == 0) {
        s.published_nodes.store(s.nodes, std::memory_order_relaxed);
        s.others_nodes = total_nodes() - s.nodes;
    }

    if (!s.can_stop) return;

    if (s.node_limit && s.nodes + s.others_nodes >= s.node_limit) {
        s.stopped = true;
        return;
    }
    if ((s.nodes & 2047) != 0) return;
    if (stop_flag.load(std::memory_order_relaxed)
        || !searching.load(std::memory_order_relaxed)
        || (s.hard_limit && !ponder_flag.load(std::memory_order_relaxed)
            && s.time_used() >= s.hard_limit))
        s.stopped = true;
}

// ---------------------------------------------------------------------------
// Quiescence search.  Stopping the main search at a fixed depth would evaluate
// positions in the middle of a capture sequence and believe the resulting
// material count -- the "horizon effect".  This extension keeps going until the
// position is quiet, considering captures and promotions, and every evasion
// when in check.  Results go into the transposition table at depth 0.
// ---------------------------------------------------------------------------

int qsearch(Searcher& s, Position& pos, int alpha, int beta, int ply) {
    check_time(s);
    if (s.stopped) return 0;
    if (ply >= MAX_PLY) return Eval::evaluate(pos);

    ++s.nodes;
    s.seldepth = std::max(s.seldepth, ply);

    const bool is_pv = (beta - alpha) > 1;
    const int original_alpha = alpha;

    // In check, "standing pat" isn't an option: the side to move can't simply
    // decline to act.  Every evasion is searched, and having none is mate.
    const bool in_check = pos.in_check();

    // ---- transposition table probe --------------------------------------
    // Any stored result is at least as deep as quiescence.
    bool tt_hit = false;
    TTData tt_data;
    tt_probe(pos.key(), tt_hit, tt_data);
    Move tt_move = MOVE_NONE;
    if (tt_hit) {
        tt_move = tt_data.move;
        int score = score_from_tt(tt_data.score, ply);
        if (!is_pv && tt_cutoff(tt_data, score, alpha, beta)) return score;
    }

    int best = -VALUE_INFINITE;
    int static_eval = -VALUE_INFINITE;
    int raw_eval = -VALUE_INFINITE;   // before the fifty-move fade; what the table keeps
    if (!in_check) {
        // Standing pat: the side to move is not obliged to capture, so the
        // static evaluation is a lower bound on what it can achieve.
        raw_eval = tt_hit ? tt_data.eval : Eval::evaluate_unscaled(pos);
        static_eval = Eval::fifty_move_scale(raw_eval, pos);
        if (static_eval >= beta) return static_eval;
        if (static_eval > alpha) alpha = static_eval;
        best = static_eval;
    }

    MoveList list;
    generate_pseudo_legal(pos, list, in_check ? GEN_ALL : GEN_CAPTURES);
    score_moves(s, pos, list, tt_move, ply);

    Move best_move = MOVE_NONE;
    int legal_moves = 0;
    for (int i = 0; i < list.size(); ++i) {
        Move m = pick_move(list, i);
        if (!pos.is_legal(m)) continue;
        ++legal_moves;

        if (!in_check && !m.is_promotion()) {
            // A capture that loses material once the exchange is played out
            // cannot improve on standing pat.
            if (list[i].score < 0) continue;

            // Delta pruning: if winning the piece outright still leaves us far
            // below alpha, the capture cannot rescue the position.
            PieceType victim = m.is_en_passant() ? PAWN : type_of(pos.piece_on(m.to()));
            if (static_eval + Eval::PIECE_VALUE[victim] + 200 < alpha) continue;
        }

        s.stack[ply].move = m;
        s.stack[ply].piece = pos.piece_on(m.from());
        pos.make_move(m);
        int score = -qsearch(s, pos, -beta, -alpha, ply + 1);
        pos.unmake_move(m);

        if (s.stopped) return 0;
        if (score > best) {
            best = score;
            best_move = m;
            if (score > alpha) alpha = score;
            if (alpha >= beta) break;
        }
    }

    if (in_check && legal_moves == 0)
        return mated_in(ply);

    const uint8_t bound = (best >= beta) ? BOUND_LOWER
                        : (best > original_alpha) ? BOUND_EXACT
                        : BOUND_UPPER;
    bool store_hit = false;
    TTData store_old;
    TTEntry* slot = tt_probe(pos.key(), store_hit, store_old);
    tt_store(slot, store_old, store_hit, pos.key(), score_to_tt(best, ply),
             raw_eval, best_move, 0, bound);
    return best;
}

// ---------------------------------------------------------------------------
// Main alpha-beta search (negamax formulation with principal variation search).
// ---------------------------------------------------------------------------

int negamax(Searcher& s, Position& pos, int depth, int alpha, int beta, int ply, bool allow_null) {
    const bool is_pv = (beta - alpha) > 1;
    const bool root = (ply == 0);

    // Stop before indexing past the PV tables in very deep lines.
    if (ply >= MAX_PLY) return Eval::evaluate(pos);

    s.pv_length[ply] = ply;

    check_time(s);
    if (s.stopped) return 0;

    if (!root) {
        // Draws by rule are scored by draw_value() (level, adjusted for
        // contempt), regardless of material.
        if (pos.is_repetition(ply) || pos.is_insufficient_material())
            return draw_value(s, pos.side_to_move());

        // The fifty-move rule does not apply when the move that reached the
        // hundredth half-move gave checkmate: mate takes precedence.  Only
        // checked when the clock has run out, so the move generation is rare.
        if (pos.is_fifty_move_draw()) {
            if (!pos.in_check()) return draw_value(s, pos.side_to_move());
            MoveList evasions;
            generate_legal(pos, evasions);
            return evasions.empty() ? mated_in(ply) : draw_value(s, pos.side_to_move());
        }

        // Mate distance pruning: a faster mate already found elsewhere makes
        // this whole subtree irrelevant.
        alpha = std::max(alpha, mated_in(ply));
        beta = std::min(beta, mate_in(ply + 1));
        if (alpha >= beta) return alpha;
    }

    const bool in_check = pos.in_check();

    // Being in check means the position is volatile; searching one ply deeper
    // costs little and avoids evaluating in the middle of a forcing sequence.
    if (in_check) ++depth;

    if (depth <= 0) return qsearch(s, pos, alpha, beta, ply);

    ++s.nodes;
    s.seldepth = std::max(s.seldepth, ply);

    // A singular extension search: this same node, searched without its best
    // move to see whether anything else comes close.  Its result describes a
    // different question, so it neither takes a cutoff from the table nor
    // stores one, and skips the pruning that assumes a normal node.
    const Move excluded = s.stack[ply].excluded;
    const bool singular_search = !excluded.is_none();

    // ---- transposition table probe --------------------------------------
    bool tt_hit = false;
    TTData tt_data;
    tt_probe(pos.key(), tt_hit, tt_data);
    Move tt_move = MOVE_NONE;

    if (tt_hit) {
        tt_move = tt_data.move;
        if (!is_pv && !singular_search && tt_data.depth >= depth) {
            int score = score_from_tt(tt_data.score, ply);
            if (tt_cutoff(tt_data, score, alpha, beta)) return score;
        }
    }

    // The table stores the static evaluation too, which saves recomputing it.
    // It stores it before the fifty-move fade, because its key does not include
    // the clock: the same position reached with a different clock must not be
    // given the other clock's evaluation.
    const int raw_eval = in_check ? -VALUE_INFINITE
                       : tt_hit ? tt_data.eval
                       : Eval::evaluate_unscaled(pos);
    const int static_eval = in_check ? -VALUE_INFINITE
                          : Eval::fifty_move_scale(raw_eval, pos);

    // ---- internal iterative reduction -------------------------------------
    // Without a move from the table, move ordering here is guesswork, so a
    // full-depth search would be expensive and poorly ordered.  Search one ply
    // shallower; the next iteration will find a stored move.
    if (depth >= 4 && tt_move.is_none()) --depth;

    // ---- forward pruning --------------------------------------------------
    if (!is_pv && !in_check && !singular_search) {
        // Reverse futility: if we are so far ahead that even conceding a large
        // margin leaves us above beta, assume the opponent cannot claw back.
        if (depth <= 6 && static_eval - 90 * depth >= beta)
            return static_eval;

        // Null move: give the opponent a free move.  If our position is still
        // above beta after that, the real move will be at least as good.  It is
        // unsound in zugzwang, hence the requirement for a non-pawn piece.
        if (allow_null && depth >= 3 && static_eval >= beta
            && pos.has_non_pawn_material(pos.side_to_move())) {
            int reduction = 2 + depth / 4;
            s.stack[ply].move = MOVE_NONE;
            pos.make_null_move();
            int score = -negamax(s, pos, depth - 1 - reduction, -beta, -beta + 1, ply + 1, false);
            pos.unmake_null_move();

            if (s.stopped) return 0;
            if (score >= beta)
                return score >= VALUE_MATE_IN_MAX_PLY ? beta : score;
        }
    }

    // ---- move loop --------------------------------------------------------
    MoveList list;
    generate_pseudo_legal(pos, list, GEN_ALL);
    score_moves(s, pos, list, tt_move, ply);

    const Color us = pos.side_to_move();
    const Move counter = counter_move(s, ply);

    // Low-depth pruning of quiet moves is only safe away from the principal
    // variation, out of check, and when no mate score is at stake.
    const bool can_prune = !is_pv && !in_check && std::abs(alpha) < VALUE_MATE_IN_MAX_PLY;
    const bool futile = can_prune && depth <= 3 && static_eval + FUTILITY_MARGIN[depth] <= alpha;

    int best_score = -VALUE_INFINITE;
    Move best_move = MOVE_NONE;
    int legal_moves = 0;
    int original_alpha = alpha;
    MoveList quiets_tried;

    // Captures searched at this node, for the capture history: the move, the
    // piece that made it and what it took (read before the move is made).
    struct TriedCapture { Move move; Piece piece; PieceType victim; };
    TriedCapture captures_tried[32];
    int captures_count = 0;

    for (int i = 0; i < list.size(); ++i) {
        Move m = pick_move(list, i);
        if (m == excluded || !pos.is_legal(m)) continue;

        if (root) {
            // "go searchmoves": the other root moves are not considered.
            if (!s.searchmoves.empty()
                && std::find(s.searchmoves.begin(), s.searchmoves.end(), m) == s.searchmoves.end())
                continue;

            // On a long search, show which root move is being examined.
            if (s.can_stop && s.elapsed() >= CURRMOVE_DELAY_MS)
                print_line("info depth " + std::to_string(s.root_depth) + " currmove " + m.to_uci()
                           + " currmovenumber " + std::to_string(legal_moves + 1));
        }

        ++legal_moves;

        const bool is_quiet = !m.is_capture() && !m.is_promotion();
        const bool is_refutation = m == s.stack[ply].killers[0] || m == s.stack[ply].killers[1] || m == counter;
        const Piece moved = pos.piece_on(m.from());
        const int history = is_quiet ? s.history[us][m.from()][m.to()] + continuation_score(s, ply, moved, m.to()) : 0;
        const PieceType victim = m.is_capture() ? captured_type(pos, m) : NO_PIECE_TYPE;

        // ---- static exchange pruning ----------------------------------------
        // Near the leaves, a move that simply loses material once the exchange
        // on its square is played out is not worth a search: a capture that
        // gives up more than a pawn per ply of depth, or a quiet move that puts
        // a piece where it is lost (the allowance grows with the square of the
        // depth, so at depth 3 a knight may still be offered).  Checked before
        // the move is made, so it costs no make/unmake.
        if (can_prune && legal_moves > 1 && depth <= SEE_PRUNE_DEPTH
            && pos.see(m) < (is_quiet ? -SEE_QUIET_MARGIN * depth * depth : -SEE_CAPTURE_MARGIN * depth))
            continue;

        // ---- singular extension -----------------------------------------
        // The table says this move is good to a decent depth.  If, with it
        // left out, a shallow search finds nothing within a margin of its
        // score, the move is the only good one here ("singular") and is
        // searched a ply deeper, so a forced line is not cut short.  If even
        // the alternatives beat beta, several moves refute the opponent's
        // last move and the node can be cut at once ("multi-cut").
        int extension = 0;
        if (!root && !singular_search && m == tt_move && depth >= SINGULAR_DEPTH
            && ply < 2 * s.root_depth
            && tt_data.depth >= depth - 3 && (tt_data.bound & BOUND_LOWER)) {
            const int tt_score = score_from_tt(tt_data.score, ply);
            if (std::abs(tt_score) < VALUE_MATE_IN_MAX_PLY) {
                const int singular_beta = tt_score - 2 * depth;
                s.stack[ply].excluded = m;
                const int value = negamax(s, pos, (depth - 1) / 2, singular_beta - 1, singular_beta, ply, false);
                s.stack[ply].excluded = MOVE_NONE;
                if (s.stopped) return 0;
                if (value < singular_beta)
                    extension = 1;
                else if (singular_beta >= beta)
                    return singular_beta;
            }
        }
        const int new_depth = depth - 1 + extension;

        s.stack[ply].move = m;
        s.stack[ply].piece = pos.piece_on(m.from());
        pos.make_move(m);
        const bool gives_check = pos.in_check();

        // ---- low-depth pruning --------------------------------------------
        // Futility: the position is too far below alpha for a quiet move to
        // recover.  Late-move pruning: enough quiet moves have been tried that
        // the rest are very unlikely to matter.  The first move is always
        // searched, and checks never pruned.
        if (can_prune && is_quiet && !gives_check && legal_moves > 1
            && (futile || (depth <= 3 && quiets_tried.size() >= LATE_MOVE_LIMIT[depth]))) {
            pos.unmake_move(m);
            continue;
        }

        if (is_quiet) quiets_tried.add(m);
        else if (m.is_capture() && captures_count < 32) captures_tried[captures_count++] = { m, moved, victim };

        int score;
        if (legal_moves == 1) {
            // The first move gets a full window; ordering means it is usually
            // the best one, and its score establishes alpha for the rest.
            score = -negamax(s, pos, new_depth, -beta, -alpha, ply + 1, true);
        } else {
            // Late move reductions: moves ordered near the end of the list are
            // unlikely to be best, so they are searched shallower first and
            // only re-searched at full depth if they surprise us.  The amount
            // grows with the logarithms of depth and move number; less on the
            // principal variation and for killer and counter moves, more or
            // less by up to two plies depending on history, and at least one
            // ply of search is always left.
            int reduction = 0;
            if (depth >= 3 && legal_moves > (is_pv ? 3 : 2) && is_quiet && !in_check && !gives_check) {
                reduction = REDUCTIONS[std::min(depth, 63)][std::min(legal_moves, 63)];
                if (is_pv) --reduction;
                if (is_refutation) --reduction;
                reduction -= history / 16384;
                reduction = std::clamp(reduction, 0, depth - 2);
            }

            score = -negamax(s, pos, new_depth - reduction, -alpha - 1, -alpha, ply + 1, true);

            if (score > alpha && reduction > 0)
                score = -negamax(s, pos, new_depth, -alpha - 1, -alpha, ply + 1, true);

            if (score > alpha && score < beta)
                score = -negamax(s, pos, new_depth, -beta, -alpha, ply + 1, true);
        }

        pos.unmake_move(m);
        if (s.stopped) return 0;

        if (score > best_score) {
            best_score = score;
            best_move = m;

            if (score > alpha) {
                alpha = score;

                // Record the principal variation for reporting.
                s.pv[ply][ply] = m;
                for (int next = ply + 1; next < s.pv_length[ply + 1]; ++next)
                    s.pv[ply][next] = s.pv[ply + 1][next];
                s.pv_length[ply] = s.pv_length[ply + 1];
            }

            if (alpha >= beta) {
                // A quiet move that causes a cutoff is worth remembering: as a
                // killer for sibling nodes at this ply, as the counter to the
                // opponent's last move, and in the history table for the same
                // from/to square anywhere in the tree.
                const int bonus = std::min(8 * depth * depth, 1600);
                if (is_quiet) {
                    if (s.stack[ply].killers[0] != m) {
                        s.stack[ply].killers[1] = s.stack[ply].killers[0];
                        s.stack[ply].killers[0] = m;
                    }
                    if (ply > 0 && !s.stack[ply - 1].move.is_none())
                        s.counter_moves[s.stack[ply - 1].piece][s.stack[ply - 1].move.to()] = m;

                    // The quiet moves tried before it failed to cut off, so they
                    // lose as much as the cutoff move gains -- in the plain
                    // history and in the continuation history alike.
                    update_quiet_histories(s, ply, us, m, moved, bonus);
                    for (const ScoredMove& q : quiets_tried)
                        if (q.move != m)
                            update_quiet_histories(s, ply, us, q.move, pos.piece_on(q.move.from()), -bonus);
                } else if (m.is_capture()) {
                    update_history(s.capture_history[moved][m.to()][victim], bonus);
                }

                // Captures tried before the move that cut off were not good
                // enough, whatever kind of move that was.
                for (int c_index = 0; c_index < captures_count; ++c_index)
                    if (const TriedCapture& c = captures_tried[c_index]; c.move != m)
                        update_history(s.capture_history[c.piece][c.move.to()][c.victim], -bonus);
                break;
            }
        }
    }

    // No legal move: checkmate if we are in check, stalemate otherwise.  The
    // mate score includes the ply so that shorter mates are preferred.
    // In a singular search the excluded move was the only one: nothing else
    // reaches the margin.
    if (legal_moves == 0)
        return singular_search ? alpha
             : in_check ? mated_in(ply) : draw_value(s, pos.side_to_move());

    // ---- store ------------------------------------------------------------
    // Probe again: the entry found above may have been reused deeper in the tree.
    const uint8_t bound = (best_score >= beta) ? BOUND_LOWER
                        : (best_score > original_alpha) ? BOUND_EXACT
                        : BOUND_UPPER;
    if (!singular_search) {
        bool store_hit = false;
        TTData store_old;
        TTEntry* slot = tt_probe(pos.key(), store_hit, store_old);
        tt_store(slot, store_old, store_hit, pos.key(), score_to_tt(best_score, ply),
                 raw_eval, best_move, depth, bound);
    }

    return best_score;
}

// The principal variation -- the line the engine expects both sides to play --
// as UCI move text, read out of the triangular table the search filled in.
std::string pv_string(const Searcher& s) {
    std::string out;
    for (int i = 0; i < s.pv_length[0]; ++i) {
        if (i) out += ' ';
        out += s.pv[0][i].to_uci();
    }
    return out;
}

// A score as UCI reports it: centipawns, or "mate n" in moves (not plies) when
// the score is close enough to the mate value to be one.
std::string score_to_uci(int score) {
    if (std::abs(score) >= VALUE_MATE_IN_MAX_PLY) {
        int plies_to_mate = VALUE_MATE - std::abs(score);
        return "mate " + std::to_string((score > 0 ? 1 : -1) * (plies_to_mate + 1) / 2);
    }
    return "cp " + std::to_string(score);
}

// Per mille of the table filled by the current search, estimated from its first
// thousand entries.
int hashfull() {
    const size_t buckets = std::min<size_t>(tt.size(), 1000 / BUCKET_SIZE);
    if (buckets == 0) return 0;
    size_t used = 0;
    for (size_t i = 0; i < buckets; ++i)
        for (const TTEntry& e : tt[i].entries) {
            const TTData d = unpack(e.data.load(std::memory_order_relaxed));
            if (d.depth >= 0 && d.age == (tt_age & TT_AGE_MASK)) ++used;
        }
    return int(used * 1000 / (buckets * BUCKET_SIZE));
}

// One "info" line.  `bound` is "", " lowerbound" or " upperbound"; the PV is
// left out when there is none.
void report(Searcher& s, int depth, int score, const char* bound, const std::string& pv) {
    const int64_t ms = s.elapsed();

    // Report what every thread has searched, not just this one.
    s.published_nodes.store(s.nodes, std::memory_order_relaxed);
    const uint64_t nodes = total_nodes();

    std::string line = "info depth " + std::to_string(depth)
                     + " seldepth " + std::to_string(std::max(s.seldepth, depth))
                     + " score " + score_to_uci(score) + bound
                     + " nodes " + std::to_string(nodes)
                     + " time " + std::to_string(ms)
                     + " nps " + std::to_string(ms > 0 ? nodes * 1000 / uint64_t(ms) : 0)
                     + " hashfull " + std::to_string(hashfull());
    if (!pv.empty()) line += " pv " + pv;
    print_line(line);
}

// Turns the limits from "go" into the two times the search works with: a soft
// limit, past which a new iteration is not begun, and a hard limit, at which
// the current one is abandoned.  Both stay zero when there is nothing to
// measure (an infinite search, or a depth or node limit with no clock).
void set_time_limits(Searcher& s, const Position& pos, const Limits& limits) {
    s.soft_limit = s.hard_limit = 0;
    s.scale_soft_limit = false;
    if (limits.infinite) return;

    if (limits.movetime) {
        // Use up to the whole budget, but don't begin a new depth once half of
        // it has gone: the next depth usually costs more than all the earlier
        // ones together, and an unfinished iteration is thrown away.
        s.hard_limit = std::max<int64_t>(limits.movetime - move_overhead_ms, 1);
        s.soft_limit = std::max<int64_t>(s.hard_limit / 2, 1);
        return;
    }

    if (!limits.has_clock) return;

    // What is left once the overhead is kept back.  A move must be returned
    // even with the clock at (or below) zero, so there is always a little.
    Color us = pos.side_to_move();
    int64_t remaining = std::max<int64_t>(limits.time[us] - move_overhead_ms, 1);
    int64_t increment = limits.inc[us];
    int moves_left = limits.movestogo > 0 ? limits.movestogo : 30;

    // Expect to spend a slice of the remaining clock plus most of the
    // increment.  How much of that is really used depends on the position:
    // iterate() scales the soft limit up while the best move keeps changing or
    // the score is falling, and down once the move is settled, so the hard
    // limit leaves room for up to two and a half times the budget -- but never
    // more than a third of what is left.
    int64_t budget = remaining / moves_left + increment * 3 / 4;
    budget = std::min(budget, remaining / 3);
    budget = std::max<int64_t>(budget, 1);

    s.soft_limit = std::max<int64_t>(budget * 3 / 5, 1);                // do not start an iteration past this
    s.hard_limit = std::max<int64_t>(std::min(budget * 5 / 2, remaining / 3), 1);  // abandon the current one past this
    s.scale_soft_limit = true;
}

size_t tt_megabytes = 0;

// The largest power-of-two number of buckets that fits in `megabytes`.
void allocate_tt(size_t megabytes) {
    size_t buckets = (megabytes * 1024 * 1024) / sizeof(Bucket);
    size_t power = 1;
    while (power * 2 <= buckets) power *= 2;

    // Built separately and swapped in: entries hold atomics, which cannot be
    // copied, and this way a failed allocation leaves the old table alone.
    std::vector<Bucket> fresh(power);
    tt.swap(fresh);
    tt_mask = power - 1;
}

// One thread's iterative deepening: search depth 1, then 2, and so on, until a
// limit stops it.
//
// Thread 0 is the one that counts -- its move is the one played, and it is the
// only thread that reports to the GUI.  The helpers search the same position
// with their own history, killers and counter moves, and skip depths on their
// own pattern, so they arrive at positions in a different order.  Everything
// they work out reaches thread 0 through the shared transposition table, and
// that is the whole of the cooperation: nobody is handed a part of the tree.
void iterate(Searcher& s, const Limits& limits) {
    Position& pos = s.root;
    Result& result = s.result;

    MoveList root_moves;
    generate_legal(pos, root_moves);

    // Already checkmated or stalemated: say so, and answer "bestmove 0000".
    if (root_moves.empty()) {
        if (s.id == 0)
            print_line(std::string("info depth 0 score ") + (pos.in_check() ? "mate 0" : "cp 0"));
        return;
    }

    // "go searchmoves": keep the legal moves it names, once each.  If none of
    // them is legal, every move is searched.
    s.searchmoves.clear();
    for (Move m : limits.searchmoves) {
        bool legal = false;
        for (const ScoredMove& sm : root_moves)
            if (sm.move == m) legal = true;
        if (legal && std::find(s.searchmoves.begin(), s.searchmoves.end(), m) == s.searchmoves.end())
            s.searchmoves.push_back(m);
    }
    const int root_count = s.searchmoves.empty() ? root_moves.size() : int(s.searchmoves.size());
    result.best = s.searchmoves.empty() ? root_moves[0].move : s.searchmoves[0];

    const int max_depth = limits.depth ? limits.depth : MAX_PLY - 1;
    int score = 0;
    std::string pv;

    // For the time management: how many iterations in a row have ended on the
    // same best move, and the score of the previous one.
    int stable_iterations = 0;
    Move previous_best = MOVE_NONE;
    int previous_score = 0;

    for (int depth = 1; depth <= max_depth; ++depth) {
        if (s.id > 0) {
            const int i = (s.id - 1) % 20;
            if (((depth + SKIP_PHASE[i]) / SKIP_SIZE[i]) % 2) continue;
        }

        s.root_depth = depth;
        int alpha = -VALUE_INFINITE;
        int beta = VALUE_INFINITE;
        int window = 25;

        // Aspiration windows: assume the score is close to the last iteration's
        // and search a narrow band around it, widening only on a fail.
        if (depth >= 4) {
            alpha = score - window;
            beta = score + window;
        }

        while (true) {
            int value = negamax(s, pos, depth, alpha, beta, 0, true);
            if (s.stopped) break;

            // On a long search a failed window is reported as a bound, so the
            // GUI hears about a changing score before the depth completes.
            const bool show_bound = s.id == 0 && s.can_stop
                                 && s.elapsed() >= BOUND_REPORT_DELAY_MS;

            if (value <= alpha) {
                if (show_bound) report(s, depth, value, " upperbound", "");
                beta = (alpha + beta) / 2;
                alpha = std::max(value - window, -VALUE_INFINITE);
                window *= 2;
            } else if (value >= beta) {
                if (show_bound) report(s, depth, value, " lowerbound", pv_string(s));
                beta = std::min(value + window, VALUE_INFINITE);
                window *= 2;
            } else {
                score = value;
                break;
            }
        }

        if (s.stopped) break;

        result.best = s.pv[0][0];
        result.score = score;
        result.depth = depth;
        if (s.pv_length[0] > 1) result.ponder = s.pv[0][1];
        pv = pv_string(s);

        // From here on a stop request, the node limit or the clock may end the search.
        s.can_stop = true;

        if (s.id == 0) report(s, depth, score, "", pv);

        // A forced mate has been proven; searching deeper cannot improve on it.
        // "go mate n" only ends on a mate in n moves or fewer for the side to move.
        if (limits.mate) {
            if (score >= mate_in(2 * limits.mate - 1)) break;
        } else if (std::abs(score) >= VALUE_MATE_IN_MAX_PLY) {
            break;
        }

        // A stop that arrived while depth 1 was still running, or -- for a
        // helper -- the main thread having finished with this search.
        if (stop_flag.load() || !searching.load()) break;

        if (s.node_limit && s.nodes + s.others_nodes >= s.node_limit) break;

        // The clock doesn't run while pondering.
        const bool timed = s.soft_limit && !ponder_flag.load();

        // With a single legal move there is nothing to decide: answer at once
        // instead of spending the clock on it.
        if (timed && root_count == 1) break;

        // On a clock, spend more time while the search is unsettled and less
        // once it is not.  A best move that just changed may not be the last
        // word, and a falling score means trouble the earlier iterations did
        // not see; a move that has survived several iterations is unlikely to
        // change with one more.
        double scale = 1.0;
        if (s.scale_soft_limit) {
            stable_iterations = (result.best == previous_best) ? stable_iterations + 1 : 0;
            scale = STABILITY_SCALE[std::min(stable_iterations, 4)];
            if (depth > 4 && previous_score - score > SCORE_DROP_MARGIN) scale *= SCORE_DROP_SCALE;
        }
        previous_best = result.best;
        previous_score = score;

        // Starting another iteration we cannot finish just wastes the clock.
        if (timed && s.time_used() >= int64_t(double(s.soft_limit) * scale)) break;
    }

    // Stopped in the middle of an iteration: a last line with the final node
    // count and time, for the deepest completed depth.
    if (s.id == 0 && s.stopped) report(s, result.depth, result.score, "", pv);
}

} // namespace

// The stop flag, set by the UCI thread and polled by the search.  It is the
// only thing the two threads share besides the output lock.
void request_stop() { stop_flag.store(true); }

void clear_stop() { stop_flag.store(false); }

bool stop_requested() { return stop_flag.load(); }

void set_pondering(bool on) { ponder_flag.store(on); }

void ponderhit() {
    // Restart the clock before leaving pondering, so the time limits are never
    // measured from the start of the ponder search.
    clock_start_ms.store(now_ms());
    ponder_flag.store(false);
}

bool pondering() { return ponder_flag.load(); }

void set_move_overhead(int milliseconds) { move_overhead_ms = std::max(milliseconds, 0); }

void set_contempt(int centipawns) { contempt_cp = std::clamp(centipawns, 0, MAX_CONTEMPT); }

int contempt() { return contempt_cp; }

void print_line(const std::string& line) {
    std::lock_guard<std::mutex> lock(output_mutex);
    std::fwrite(line.data(), 1, line.size(), stdout);
    std::fputc('\n', stdout);
    std::fflush(stdout);
}

bool init(size_t megabytes) {
    megabytes = std::clamp<size_t>(megabytes, 1, MAX_HASH_MB);

    // Release the old table before asking for the new one, then halve the
    // request until the memory is there.
    std::vector<Bucket>().swap(tt);
    for (size_t mb = megabytes;; mb /= 2) {
        try {
            allocate_tt(mb);
            tt_megabytes = mb;
            return mb == megabytes;
        } catch (const std::bad_alloc&) {
            if (mb <= 1) throw;
        }
    }
}

size_t hash_megabytes() { return tt_megabytes; }

// How many threads search at once.  Only called between searches; the
// searchers are kept afterwards, so their history tables survive from move to
// move.
void set_threads(int count) {
    thread_count = std::clamp(count, 1, MAX_THREADS);
    searchers.clear();
    for (int i = 0; i < thread_count; ++i) {
        searchers.push_back(std::make_unique<Searcher>());
        searchers.back()->id = i;
    }
}

int threads() { return thread_count; }

// Forgets everything learned from previous searches: the transposition table,
// the pawn hash table, and every thread's history scores, counter moves and
// killers.  "ucinewgame" calls this so that one game cannot influence the next.
//
// The pawn hash table is per thread, so only this thread's is emptied here; the
// helpers' entries are checked against the actual pawns before they are used,
// so leaving them costs nothing but the memory they already occupy.
void clear() {
    for (Bucket& bucket : tt)
        for (TTEntry& e : bucket.entries) {
            e.key.store(0, std::memory_order_relaxed);
            e.data.store(0, std::memory_order_relaxed);
        }
    Eval::clear();
    for (auto& searcher : searchers)
        searcher->clear_heuristics();
    tt_age = 0;
}

// The whole search: give every thread its own copy of the position, start the
// helpers, and do thread 0's share here.  When thread 0 is finished, the
// helpers are told to stop and joined, and thread 0's move is the answer.
//
// The threads are never waited on individually and never synchronise with each
// other: everything they share goes through the transposition table.  That is
// what makes this "lazy" SMP -- simple, and it works because a position the
// helpers have already searched is one thread 0 gets for free.
Result think(Position& pos, const Limits& limits) {
    if (tt.empty()) init(64);
    if (searchers.empty()) set_threads(thread_count);

    ++tt_age;
    clock_start_ms.store(now_ms());
    const auto start = std::chrono::steady_clock::now();

    for (auto& searcher : searchers) {
        searcher->prepare(pos, limits, start);
        set_time_limits(*searcher, pos, limits);
    }

    searching.store(true);

    std::vector<std::thread> helpers;
    for (size_t i = 1; i < searchers.size(); ++i)
        helpers.emplace_back([&limits, i] { iterate(*searchers[i], limits); });

    iterate(*searchers[0], limits);

    // Thread 0 has its answer, so the helpers' work is no longer needed.
    searching.store(false);
    for (std::thread& helper : helpers)
        helper.join();

    Searcher& main_thread = *searchers[0];
    main_thread.published_nodes.store(main_thread.nodes, std::memory_order_relaxed);

    Result result = main_thread.result;
    result.nodes = total_nodes();

    // When the line was cut short (usually by a table hit), take the reply to
    // the best move from the table, so a GUI still has a move to ponder on.
    if (result.ponder.is_none() && !result.best.is_none()) {
        pos.make_move(result.best);
        bool hit = false;
        TTData data;
        tt_probe(pos.key(), hit, data);
        if (hit && !data.move.is_none()) {
            MoveList replies;
            generate_legal(pos, replies);
            for (const ScoredMove& r : replies)
                if (r.move == data.move) result.ponder = r.move;
        }
        pos.unmake_move(result.best);
    }

    return result;
}

} // namespace Search
