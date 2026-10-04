#include "bitboard.h"
#include "eval.h"
#include "movegen.h"
#include "perft.h"
#include "position.h"
#include "search.h"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <iostream>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

namespace {

const char* ENGINE_NAME = "Bitboard Engine 11";
const char* ENGINE_AUTHOR = "built from scratch in C++";

// How long a plain "go" (no limits at all) thinks, in milliseconds.  The GUI
// sends a plain "go" and leaves the decision to the engine; change it here.
constexpr int64_t DEFAULT_MOVE_TIME_MS = 500;

constexpr int DEFAULT_HASH_MB = 64;
constexpr int DEFAULT_MOVE_OVERHEAD_MS = 10;
constexpr int DEFAULT_CONTEMPT = 25;
constexpr int MAX_MOVE_OVERHEAD_MS = 5000;

// The search runs on this thread, so "stop", "isready", "ponderhit" and "quit"
// are read and answered while it thinks.
std::thread search_thread;

// True while the running search came from "go infinite": it never ends by
// itself, so a command that waits for it would wait forever.
bool search_is_infinite = false;

// Waits for the search thread to finish, and the same with a stop request
// first.  Every path that ends a search goes through one of these two, so the
// engine can never leave a thread running behind it.
void wait_for_search() {
    if (search_thread.joinable()) search_thread.join();
}

void stop_search() {
    Search::request_stop();
    wait_for_search();
    Search::set_pondering(false);
}

// Case folding for option names, and whitespace trimming for option values and
// error messages -- UCI option names are matched case-insensitively.
std::string to_lower(std::string text) {
    for (char& c : text) c = char(std::tolower(static_cast<unsigned char>(c)));
    return text;
}

std::string trim(const std::string& text) {
    const size_t first = text.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) return "";
    const size_t last = text.find_last_not_of(" \t\r\n");
    return text.substr(first, last - first + 1);
}

// Parses a whole token as an integer; false for anything else ("abc", "12x").
bool parse_int(const std::string& token, int64_t& value) {
    try {
        size_t used = 0;
        const long long parsed = std::stoll(token, &used);
        if (used != token.size()) return false;
        value = parsed;
        return true;
    } catch (...) {
        return false;
    }
}

// The words the engine answers to, and the words that introduce a parameter of
// "go".  The first is what lets unknown leading tokens be skipped, the second
// is what lets "searchmoves" know where its list of moves ends.
bool is_command(const std::string& token) {
    static const char* COMMANDS[] = {
        "uci", "debug", "isready", "setoption", "register", "ucinewgame", "position",
        "go", "stop", "ponderhit", "quit",
        "d", "display", "eval", "moves", "perft", "test", "bench",
    };
    return std::find(std::begin(COMMANDS), std::end(COMMANDS), token) != std::end(COMMANDS);
}

bool is_go_keyword(const std::string& token) {
    static const char* KEYWORDS[] = {
        "searchmoves", "ponder", "wtime", "btime", "winc", "binc", "movestogo",
        "depth", "nodes", "mate", "movetime", "infinite", "perft",
    };
    return std::find(std::begin(KEYWORDS), std::end(KEYWORDS), token) != std::end(KEYWORDS);
}

// "position [startpos | fen <fen>] [moves <m1> <m2> ...]"
//
// The new position is built on the side and only replaces the current one if
// the FEN is valid, so a bad command can't leave the engine with a broken board.
void cmd_position(Position& pos, std::istringstream& in) {
    Position next;
    std::string token;
    in >> token;

    bool has_moves = false;
    if (token == "startpos") {
        in >> token;
        has_moves = (token == "moves");
    } else if (token == "fen") {
        std::string fen, part;
        while (in >> part) {
            if (part == "moves") { has_moves = true; break; }
            fen += part + " ";
        }
        if (!next.set_from_fen(fen)) {
            Search::print_line("info string invalid FEN, position unchanged: " + trim(fen));
            return;
        }
    } else {
        Search::print_line("info string position needs \"startpos\" or \"fen\"");
        return;
    }

    if (has_moves)
        while (in >> token) {
            Move m = next.parse_uci_move(token);
            if (m.is_none()) {
                Search::print_line("info string illegal move in position command: " + token);
                break;
            }
            next.make_move(m);
        }

    pos = next;
}

// "go [searchmoves <m1> ...] [ponder] [wtime ms] [btime ms] [winc ms] [binc ms]
//     [movestogo n] [depth n] [nodes n] [mate n] [movetime ms] [infinite]"
void cmd_go(Position& pos, std::istringstream& in) {
    std::vector<std::string> tokens;
    for (std::string token; in >> token;) tokens.push_back(token);

    Search::Limits limits;

    // The number after a keyword; 0 when it is missing or not a number.
    auto number = [&](size_t& i) -> int64_t {
        int64_t value = 0;
        if (i + 1 < tokens.size() && parse_int(tokens[i + 1], value)) ++i;
        return value;
    };

    for (size_t i = 0; i < tokens.size(); ++i) {
        const std::string& token = tokens[i];

        if (token == "depth") {
            const int64_t d = number(i);
            limits.depth = int(std::clamp<int64_t>(d, 1, MAX_PLY - 1));
        } else if (token == "movetime") {
            limits.movetime = std::max<int64_t>(number(i), 1);
        } else if (token == "wtime") {
            limits.time[WHITE] = number(i);
            limits.has_clock = true;
        } else if (token == "btime") {
            limits.time[BLACK] = number(i);
            limits.has_clock = true;
        } else if (token == "winc") {
            limits.inc[WHITE] = std::max<int64_t>(number(i), 0);
        } else if (token == "binc") {
            limits.inc[BLACK] = std::max<int64_t>(number(i), 0);
        } else if (token == "movestogo") {
            limits.movestogo = int(std::clamp<int64_t>(number(i), 0, 1000));
        } else if (token == "nodes") {
            limits.nodes = uint64_t(std::max<int64_t>(number(i), 1));
        } else if (token == "mate") {
            limits.mate = int(std::clamp<int64_t>(number(i), 1, MAX_PLY / 2));
        } else if (token == "infinite") {
            limits.infinite = true;
        } else if (token == "ponder") {
            limits.ponder = true;
        } else if (token == "searchmoves") {
            // Moves follow until the next keyword.
            while (i + 1 < tokens.size() && !is_go_keyword(tokens[i + 1])) {
                const std::string& text = tokens[++i];
                Move m = pos.parse_uci_move(text);
                if (m.is_none())
                    Search::print_line("info string searchmoves: not a legal move: " + text);
                else
                    limits.searchmoves.push_back(m);
            }
        } else if (token == "perft") {
            perft_divide(pos, int(std::clamp<int64_t>(number(i), 1, 20)));
            return;
        } else {
            Search::print_line("info string go: unknown parameter ignored: " + token);
        }
    }

    // Without any limit the engine decides: think for a fixed time rather than
    // a fixed depth, which takes milliseconds in one position and seconds in
    // another.  After "go ponder" this is the time used once "ponderhit" comes.
    if (!limits.depth && !limits.movetime && !limits.infinite && !limits.has_clock
        && !limits.nodes && !limits.mate)
        limits.movetime = DEFAULT_MOVE_TIME_MS;

    Search::clear_stop();
    Search::set_pondering(limits.ponder);
    search_is_infinite = limits.infinite;

    // The search works on its own copy of the position, so nothing it touches
    // is shared with this thread while it runs.
    search_thread = std::thread([root = pos, limits]() mutable {
        Search::Result result = Search::think(root, limits);

        // "go infinite" must not answer before "stop", and "go ponder" not
        // before "ponderhit" or "stop", even if the search ends early (for
        // example on a proven mate).
        while ((limits.infinite || Search::pondering()) && !Search::stop_requested())
            std::this_thread::sleep_for(std::chrono::milliseconds(1));

        std::string line = "bestmove " + result.best.to_uci();
        if (!result.ponder.is_none()) line += " ponder " + result.ponder.to_uci();
        Search::print_line(line);
    });
}

// "setoption name <id> [value <x>]" -- option names are case-insensitive and
// may contain spaces.
void cmd_setoption(std::istringstream& in) {
    std::string token, name, value;
    in >> token;                                   // "name"
    while (in >> token && token != "value")
        name += (name.empty() ? "" : " ") + token;
    std::getline(in, value);
    value = trim(value);

    const std::string key = to_lower(name);
    int64_t number = 0;

    if (key == "hash") {
        if (!parse_int(value, number)) {
            Search::print_line("info string Hash needs a number of megabytes, got: " + value);
            return;
        }
        const size_t megabytes = size_t(std::clamp<int64_t>(number, 1, Search::MAX_HASH_MB));
        if (!Search::init(megabytes))
            Search::print_line("info string not enough memory for " + std::to_string(megabytes)
                               + " MB of hash, using " + std::to_string(Search::hash_megabytes()) + " MB");
    } else if (key == "threads") {
        if (!parse_int(value, number)) {
            Search::print_line("info string Threads needs a number, got: " + value);
            return;
        }
        Search::set_threads(int(std::clamp<int64_t>(number, 1, Search::MAX_THREADS)));
    } else if (key == "contempt") {
        if (!parse_int(value, number)) {
            Search::print_line("info string Contempt needs a number of centipawns, got: " + value);
            return;
        }
        Search::set_contempt(int(std::clamp<int64_t>(number, 0, Search::MAX_CONTEMPT)));
    } else if (key == "fifty move scaling") {
        // A UCI "check" option arrives as "true" or "false".
        Eval::set_fifty_move_scaling(to_lower(value) != "false");
    } else if (key == "clear hash") {
        Search::clear();
    } else if (key == "move overhead") {
        if (!parse_int(value, number)) {
            Search::print_line("info string Move Overhead needs a number of milliseconds, got: " + value);
            return;
        }
        Search::set_move_overhead(int(std::clamp<int64_t>(number, 0, MAX_MOVE_OVERHEAD_MS)));
    } else if (key == "ponder") {
        // Only tells the engine whether the GUI will send "go ponder"; nothing
        // needs preparing.
    } else {
        Search::print_line("info string unknown option: " + name);
    }
}

// Searches a fixed set of positions to a fixed depth and reports the total node
// count and speed.  The node count is the number to compare between versions:
// it is exactly reproducible, while the timing depends on the machine.
void cmd_bench(int depth) {
    static const char* POSITIONS[] = {
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
        "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
        "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
        "4rrk1/pp1n1pp1/2p1b2p/3p4/3P4/2N1PN2/PP3PPP/2RR2K1 w - - 0 1",
    };

    uint64_t total = 0;
    auto start = std::chrono::steady_clock::now();

    // Works on its own position, so the one set up with "position" is kept.
    Position pos;
    for (const char* fen : POSITIONS) {
        pos.set_from_fen(fen);
        Search::clear();
        Search::clear_stop();
        Search::Limits limits;
        limits.depth = depth;
        total += Search::think(pos, limits).nodes;
    }

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                  std::chrono::steady_clock::now() - start).count();
    std::cout << "\nBench: " << total << " nodes in " << ms << " ms  ("
              << (ms ? total * 1000 / ms : 0) << " nps)" << std::endl;
}

// "bench [depth]": depth 9 unless a valid depth is given.
int bench_depth(const std::string& text) {
    int64_t depth = 9;
    if (!text.empty() && parse_int(text, depth)) return int(std::clamp<int64_t>(depth, 1, MAX_PLY - 1));
    return 9;
}

} // namespace

// Start-up, the shell shortcuts ("engine test", "engine bench", "engine perft")
// and then the UCI loop: read a line, answer it, repeat until "quit" or until
// the GUI closes the pipe.  The loop keeps reading while a search runs, which
// is what makes "stop", "isready" and "ponderhit" work mid-search.
int main(int argc, char** argv) {
    Bitboards::init();
    Zobrist::init();
    Eval::init();
    Search::init(DEFAULT_HASH_MB);
    Search::set_move_overhead(DEFAULT_MOVE_OVERHEAD_MS);
    Search::set_contempt(DEFAULT_CONTEMPT);

    Position pos;

    // Allow "engine perft 6", "engine bench [depth]" and "engine test" from the shell.
    if (argc > 1) {
        std::string arg = argv[1];
        if (arg == "test") return run_perft_suite() ? 0 : 1;
        if (arg == "bench") { cmd_bench(bench_depth(argc > 2 ? argv[2] : "")); return 0; }
        if (arg == "perft") {
            int64_t depth = 5;
            if (argc > 2 && !parse_int(argv[2], depth)) depth = 5;
            perft_divide(pos, int(std::clamp<int64_t>(depth, 1, 20)));
            return 0;
        }
    }

    std::string line;
    while (std::getline(std::cin, line)) {
        std::istringstream in(line);

        // UCI: tokens before the first known command are skipped, so
        // "joho debug on" is read as "debug on".
        std::string command, first;
        while (in >> command && !is_command(command))
            if (first.empty()) first = command;
        if (!is_command(command)) {
            if (!first.empty()) Search::print_line("info string unknown command: " + first);
            continue;
        }

        // These are handled at once, even during a search.
        if (command == "isready") { Search::print_line("readyok"); continue; }
        if (command == "stop") { stop_search(); continue; }
        if (command == "ponderhit") { Search::ponderhit(); continue; }
        if (command == "quit") break;
        if (command == "debug" || command == "register") continue;   // nothing to do

        // Everything else waits for a running search to finish, so the search
        // never shares the position, the hash table or stdout with this loop.
        // A search that would never finish by itself ("go infinite", or "go
        // ponder" without a "ponderhit") is stopped first instead.
        if (search_thread.joinable() && (search_is_infinite || Search::pondering()))
            stop_search();
        wait_for_search();

        if (command == "uci") {
            Search::print_line(std::string("id name ") + ENGINE_NAME);
            Search::print_line(std::string("id author ") + ENGINE_AUTHOR);
            Search::print_line("option name Hash type spin default " + std::to_string(DEFAULT_HASH_MB)
                               + " min 1 max " + std::to_string(Search::MAX_HASH_MB));
            Search::print_line("option name Threads type spin default 1 min 1 max "
                               + std::to_string(Search::MAX_THREADS));
            Search::print_line("option name Contempt type spin default "
                               + std::to_string(DEFAULT_CONTEMPT)
                               + " min 0 max " + std::to_string(Search::MAX_CONTEMPT));
            Search::print_line("option name Fifty Move Scaling type check default true");
            Search::print_line("option name Clear Hash type button");
            Search::print_line("option name Ponder type check default false");
            Search::print_line("option name Move Overhead type spin default "
                               + std::to_string(DEFAULT_MOVE_OVERHEAD_MS)
                               + " min 0 max " + std::to_string(MAX_MOVE_OVERHEAD_MS));
            Search::print_line("uciok");
        } else if (command == "ucinewgame") {
            pos.set_start_position();
            Search::clear();
        } else if (command == "setoption") {
            cmd_setoption(in);
        } else if (command == "position") {
            cmd_position(pos, in);
        } else if (command == "go") {
            cmd_go(pos, in);
        } else if (command == "d" || command == "display") {
            std::cout << pos.to_string() << std::flush;
        } else if (command == "eval") {
            std::cout << "static eval: " << Eval::evaluate(pos) << " cp (side to move)"
                      << std::endl;
        } else if (command == "moves") {
            MoveList list;
            generate_legal(pos, list);
            for (const ScoredMove& sm : list) std::cout << sm.move.to_uci() << " ";
            std::cout << "\n(" << list.size() << " legal moves)" << std::endl;
        } else if (command == "perft") {
            std::string text;
            int64_t depth = 5;
            if (in >> text && !parse_int(text, depth)) depth = 5;
            perft_divide(pos, int(std::clamp<int64_t>(depth, 1, 20)));
        } else if (command == "test") {
            run_perft_suite();
        } else if (command == "bench") {
            std::string text;
            in >> text;
            cmd_bench(bench_depth(text));
        }
    }

    // "quit", or the GUI closed our input: end any search before exiting, so the
    // engine never outlives the program that started it.
    stop_search();
    return 0;
}
