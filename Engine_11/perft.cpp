#include "perft.h"
#include "movegen.h"

#include <chrono>
#include <cstdio>
#include <string>
#include <vector>

// Counts the leaf nodes of the legal move tree at the given depth, by playing
// every legal move and recursing.  No evaluation, no pruning and no hash table:
// the whole point is to count exactly what the rules allow.
uint64_t perft(Position& pos, int depth) {
    if (depth == 0) return 1;

    MoveList list;
    generate_pseudo_legal(pos, list, GEN_ALL);

    // At depth 1 the moves only need counting, not playing.
    if (depth == 1) {
        uint64_t legal = 0;
        for (const ScoredMove& sm : list)
            if (pos.is_legal(sm.move)) ++legal;
        return legal;
    }

    uint64_t nodes = 0;
    for (const ScoredMove& sm : list) {
        if (!pos.is_legal(sm.move)) continue;
        pos.make_move(sm.move);
        nodes += perft(pos, depth - 1);
        pos.unmake_move(sm.move);
    }
    return nodes;
}

// The same count, broken down by first move, plus timing.  This is how a move
// generation bug is found: compare the per-move totals against a known-good
// engine, play the move whose total differs, and repeat one level down.
void perft_divide(Position& pos, int depth) {
    if (depth <= 0) return;

    MoveList list;
    generate_legal(pos, list);

    uint64_t total = 0;
    auto start = std::chrono::steady_clock::now();

    for (const ScoredMove& sm : list) {
        pos.make_move(sm.move);
        uint64_t nodes = (depth == 1) ? 1 : perft(pos, depth - 1);
        pos.unmake_move(sm.move);
        total += nodes;
        std::printf("%s: %llu\n", sm.move.to_uci().c_str(), (unsigned long long)nodes);
    }

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - start).count();
    std::printf("\nNodes: %llu\n", (unsigned long long)total);
    std::printf("Time:  %lld ms\n", (long long)ms);
    if (ms > 0)
        std::printf("Speed: %.2f Mnps\n", double(total) / double(ms) / 1000.0);
}

namespace {

struct PerftCase {
    const char* name;
    const char* fen;
    std::vector<uint64_t> expected;   // index 0 is depth 1
};

// The canonical positions from the Chess Programming Wiki.  Between them they
// exercise castling, en passant (including the discovered-check case), all four
// promotion pieces, pinned pieces and check evasion.
const std::vector<PerftCase> SUITE = {
    { "startpos", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
      { 20, 400, 8902, 197281, 4865609 } },
    { "kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
      { 48, 2039, 97862, 4085603 } },
    { "position 3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
      { 14, 191, 2812, 43238, 674624 } },
    { "position 4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1",
      { 6, 264, 9467, 422333 } },
    { "position 4 mirrored", "r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1",
      { 6, 264, 9467, 422333 } },
    { "position 5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8",
      { 44, 1486, 62379, 2103487 } },
    { "position 6", "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
      { 46, 2079, 89890, 3894594 } },
};

} // namespace

// Runs every position in the suite to every listed depth and reports each
// result against the published reference count.  Returns false if any of them
// disagrees, which is the exit code "engine test" reports to the shell.
bool run_perft_suite() {
    Position pos;
    bool all_passed = true;
    uint64_t total_nodes = 0;
    auto start = std::chrono::steady_clock::now();

    for (const PerftCase& test : SUITE) {
        pos.set_from_fen(test.fen);
        for (size_t i = 0; i < test.expected.size(); ++i) {
            int depth = int(i) + 1;
            uint64_t got = perft(pos, depth);
            uint64_t want = test.expected[i];
            total_nodes += got;
            bool ok = (got == want);
            all_passed &= ok;
            std::printf("%-4s %-20s depth %d  %12llu / %12llu\n",
                        ok ? "ok" : "FAIL", test.name, depth,
                        (unsigned long long)got, (unsigned long long)want);
        }
    }

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - start).count();
    std::printf("\n%s  (%llu nodes, %lld ms, %.2f Mnps)\n",
                all_passed ? "All perft tests passed." : "PERFT FAILURES.",
                (unsigned long long)total_nodes, (long long)ms,
                ms > 0 ? double(total_nodes) / double(ms) / 1000.0 : 0.0);
    return all_passed;
}
