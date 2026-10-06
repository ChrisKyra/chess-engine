# Bitboard Engine 13

A complete chess engine in C++17. It represents the board with bitboards and
magic attack tables, searches with alpha-beta (principal variation search) and a
long list of pruning, reduction and extension techniques, evaluates positions with
a hand-built evaluation whose weights are fitted to game results, knows a set of
endgames outright, and can search on several threads at once. It speaks UCI, so
it plugs into any chess GUI, and it comes with its own tools for testing and
tuning: a perft suite for the move generator, a Texel tuner for the evaluation and
an SPSA set-up for the search.

UCI name: **Bitboard Engine 13**.

Contents: [What it does](#what-it-does) · [Build](#build) · [Run](#run) ·
[UCI options](#uci-options) · [How it works](#how-it-works) · [Files](#files) ·
[Function reference](#function-reference) · [Testing and tuning](#testing-and-tuning) ·
[Known limitations](#known-limitations)

## What it does

When a GUI sends a position and `go`, the engine:

1. **Sets up the position** from the FEN and the moves played, keeping a history
   of every earlier position so it can recognise repetitions.
2. **Decides how long to think** — a fixed time per move, or a share of the clock
   that grows while the search is unsettled and shrinks once it is not.
3. **Searches** with iterative deepening: depth 1, 2, 3 ... each iteration a full
   alpha-beta search that uses everything the earlier ones stored in the
   transposition table. Each search explores a tree of moves, prunes and reduces
   the parts that cannot matter, extends the forcing ones, and at the leaves plays
   out captures (quiescence search) before evaluating.
4. **Evaluates** the positions at the leaves: material and piece placement, pawn
   structure, piece activity, king safety, threats and passed pawns, blended
   between middlegame and endgame — or, for endgames it knows, their actual result.
5. **Answers** with the best move of the deepest finished iteration (or, if the
   clock stopped an iteration that had already found a better move, that move),
   reporting depth, score, nodes and the expected line along the way.

On an Apple M4 it searches about 2.4 million positions per second on one thread
and about 10 million on four; in self-play at 8 s + 0.08 s per game, four threads
beat one by roughly 200 Elo.

## Build

```sh
make            # ./engine
make test       # build, then run the perft correctness suite
make bench      # build, then run the node-count benchmark
make tuner      # ./tuner: the engine with the evaluation trace and the Texel tuner
make spsa       # ./engine-spsa: the engine with its search parameters as UCI options
make clean
```

Only a C++17 compiler is needed. `-march=native` is on by default for the
hardware popcount and bit-scan instructions; remove it from the Makefile for a
binary that has to run on other CPUs.

## Run

```sh
./engine test          # perft suite: about 17 million nodes against published counts
./engine bench         # node-count benchmark (reproducible with one thread)
./engine bench 12      # the same at another depth (default 9)
./engine perft 6       # per-move node counts from the start position
./engine               # interactive UCI mode
```

In interactive mode, besides every UCI command, a few conveniences:

```
position startpos moves e2e4 e7e5
d                      # print the board, FEN and hash key
eval                   # static evaluation of the current position
moves                  # list the legal moves
perft 5                # per-move node counts from the current position
go                     # think for the default 500 ms
go depth 12
go movetime 3000
go wtime 300000 btime 300000 winc 2000 binc 2000
go nodes 1000000
go mate 3              # stop once a mate in 3 or fewer is found
go searchmoves e2e4 d2d4
go infinite            # until "stop"
go ponder              # no time limit until "ponderhit" (then the normal budget) or "stop"
ponderhit
stop
bench 12
```

To play against it, load the `engine` binary as a UCI engine in any GUI (the
`gui` folder of this repository, Cute Chess, Arena, BanksiaGUI, En Croissant).

## UCI options

| Option | Type | Default | What it does |
|---|---|---|---|
| `Hash` | spin 1–4096 | 128 | Size of the transposition table in MB. With several threads or long searches, more is better; if the memory is not there, the engine halves the request until it fits and says so. |
| `Clear Hash` | button | | Empties the table and every thread's history. |
| `Threads` | spin 1–64 | 1 | How many threads search at once (see [Threads](#threads)). Set it to the number of fast cores; on Apple silicon, the performance cores. With one thread the search is deterministic. |
| `Ponder` | check | false | Tells the engine the GUI may send `go ponder` (thinking on the opponent's time). |
| `Move Overhead` | spin 0–5000 | 10 | Milliseconds kept back from every move for the GUI and the connection. |
| `Contempt` | spin 0–100 | 25 | How many centipawns a draw counts against the side the engine plays, so it does not settle for a repetition in a level position. Analysis (`go infinite`) always uses 0. |
| `Fifty Move Scaling` | check | true | Fades the evaluation towards zero as the fifty-move clock runs down, so shuffling costs the better side its advantage. |

The SPSA build (`make spsa`) adds one spin option per search parameter (see
[Tuning the search](#tuning-the-search-spsa)).

## How it works

### Board representation

The position is held three ways at once, each answering a different question
fast:

- **Twelve bitboards**, one 64-bit word per piece type per colour: "where are the
  white knights" is one load. Set operations on whole groups of pieces — all
  pawns pushed one rank, every square attacked by a colour — are a handful of
  shifts and masks.
- **Occupancy bitboards** per colour and combined, kept in step with the twelve.
- **A 64-entry piece array** (a "mailbox"): "what is on e4" is one load instead of
  twelve bit tests.

Squares are numbered a1 = 0 to h8 = 63, rank by rank. Moving every piece in a set
one square north is a shift left by 8, one square east a shift left by 1 with the
a-file masked off so nothing wraps from the h-file.

A position also carries a **Zobrist key**: a 64-bit hash built by xoring one fixed
random number per piece per square, one for the side to move, one per castling
combination and one per en passant file. Making a move updates the key
incrementally (xor out what changed, xor in what is new), so every position has
its key at no cost. The key indexes the transposition table and detects
repetitions. The en passant square is part of the key only when an en passant
capture is actually legal — otherwise the same position reached by different move
orders would get different keys and repetitions would be missed.

Every move is a 16-bit number: origin (6 bits), destination (6 bits) and a 4-bit
type (quiet, double push, castle, capture, en passant, four promotions, four
capturing promotions), chosen so that "is a capture" and "is a promotion" are
single bit tests.

### Attack generation

Knights, kings and pawns cannot be blocked, so their attack sets for every square
are computed once into tables.

Bishops, rooks and queens can be blocked, and use **magic bitboards**: for each
square, mask the squares whose occupancy matters (the rays, minus the board edge
— a piece on the last square blocks nothing behind it), multiply the masked
occupancy by a square-specific "magic" number and shift; the result indexes a
table of precomputed attack sets. One multiply and one shift replace a ray walk.
The magic numbers are searched for at start-up (a few milliseconds), which keeps
the source free of tables of constants.

### Move generation

Moves are generated **pseudo-legally** — they may leave the own king in check —
and filtered afterwards, because the filter is cheap and most moves pass it.
Pawns get their own generator (pushes, double pushes, captures, en passant and
all four promotions, each as a few shifts over the whole pawn bitboard); knights,
bishops, rooks, queens and the king look up their attack set and drop squares
holding their own pieces; castling checks the rights, the empty squares between,
and that the king does not start on, cross or land on an attacked square.

The legality filter (`Position::is_legal`) never makes the move. It builds the
occupancy the move would leave and tests the king's square against it once, which
handles pins, discovered checks and the awkward en passant case (two pieces leave
one rank at once) uniformly. The search filters lazily, move by move, so a cutoff
early in the list saves the rest of the work.

### Correctness

`make test` runs **perft** — counting every legal move sequence to a fixed depth —
on seven standard positions chosen to exercise castling rights, en passant
(including the discovered-check case), every promotion, pins and check evasions:
about 17 million nodes, all matching published counts. A move-generation bug can
affect one position in ten thousand and silently corrupt everything above it, so
any change to `movegen.cpp` or `position.cpp` is only trusted after `make test`.

### Evaluation

The evaluation scores a position in centipawns from the side to move's point of
view. Every term is kept as a **(middlegame, endgame) pair**, because what is good
changes as material comes off — a king wants shelter in the middlegame and the
centre in the endgame, a rook gains value as the board empties. At the end the two
totals are blended by the **game phase** (24 with all pieces on, 0 with none:
knights and bishops count 1, rooks 2, queens 4).

The terms, in the order they are computed (later ones need the attack maps the
earlier ones build):

1. **Material and piece-square tables.** A value per piece, plus a value per
   piece per square (64 entries per piece type per phase). The knight, bishop,
   rook and queen tables are mirror-symmetric between the wings; pawns and kings
   keep both wings, because the side a king castles to matters. Current piece
   values (middlegame / endgame): pawn 81 / 114, knight 384 / 339, bishop
   384 / 384, rook 588 / 615, queen 1121 / 1216.
2. **Pawn structure** — doubled, isolated and backward pawns; connected pawns
   (defending each other or side by side, by rank, a pair side by side counting
   half again); passed pawns by rank. All of it depends only on the pawns, so it is
   cached in a **pawn hash table** keyed on both pawn bitboards (an entry is only
   used for the exact structure it was computed from).
3. **Pieces**, walked one by one, building each side's attack maps on the way:
   - mobility — safe squares reached (not attacked by enemy pawns, not holding the
     own king or an own pawn that is blocked or still on its second or third rank),
     measured against a per-piece baseline;
   - knight and bishop outposts (in the enemy half, defended by a pawn, never
     attackable by an enemy pawn), a minor piece shielded by its own pawn in front,
     and its distance from its own king;
   - a bishop's own pawns on its square colour (a bad bishop), and a bishop on the
     long diagonal (seeing two centre squares through anything but pawns);
   - rooks on open and half-open files, on the seventh rank when there is something
     to attack there, and a rook shut in on its wing by its own king;
   - the bishop pair.
4. **King safety** (middlegame): the pawn shield in front of the king, open and
   half-open files beside it, enemy pawns storming those files, squares from which
   each enemy piece type could give a check without being taken, and the attack
   units built up by enemy pieces aimed at the squares around the king — growing
   with the square of the units, counted in full only when several pieces join in,
   with extra units for squares in the king's ring the defender does not cover
   twice.
5. **Threats** — enemy pieces attacked by pawns, rooks and queens attacked by
   minor pieces, a queen attacked by a rook, and any enemy piece attacked and not
   defended.
6. **Passed pawns** (endgame) — king distances to the square in front, a free and
   a safe path, a blockader, rooks behind the pawn, and the rule of the square
   (with no enemy pieces, a pawn the enemy king cannot catch is nearly a queen).
7. **Scaling** — the endgame score is scaled down where the material cannot win:
   bishops of opposite colours, a pawnless side with a small edge, knights alone,
   and rook-and-pawn against rook or bishop-and-pawn against bishop with the
   defending king standing in front of the pawn.
8. **Tempo** — a small bonus for having the move.
9. **Fifty-move fade** — the score is multiplied by (200 − clock) / 200, so a
   position two moves from a draw by rule is worth half as much. The
   transposition table stores the score before this fade (its key does not include
   the clock) and the fade is applied whenever it is read.

**Where the weights come from.** All of these weights except the king-danger
curve, the scale factors and tempo are **fitted to game results** (Texel tuning):
the engine played itself (about 24,000 games at 10,000 nodes a move, from varied
openings), each quiet position was labelled with its game's result, and the
weights were adjusted until the evaluation, turned into an expected score, best
predicted those results. See [Tuning the evaluation](#tuning-the-evaluation-texel).

### Endgame knowledge

Some endings are recognised and scored by what they are known to be worth rather
than term by term. All of them involve a lone king:

- **King and pawn against king** is solved exactly. At start-up every such
  position (196,608 once the pawn is mirrored onto files a–d) is classified by
  retrograde analysis: positions decided on the spot (a safe promotion, stalemate,
  the pawn captured), then over and over every position whose moves all lead to
  known results, until nothing changes. The result is one bit per position. A won
  position scores as a known win (10,000 plus the pawn's value and progress), a
  drawn one as 0 — so the engine never pushes a pawn into a draw or lets a win go.
- **Mating material against a lone king** (a queen, a rook, two bishops on
  different colours, or bishop and knight) scores as a known win, plus terms that
  drive the lone king to the edge and bring the strong king close — the shape every
  mating pattern needs. Bishop and knight can only mate in a corner of the bishop's
  colour, so there the lone king is pulled towards that corner step by step along
  the edge, steeply enough that it is not left in the centre.
- **Known draws**: rook pawns with the defending king in front of them, and a
  bishop with rook pawns whose queening square the bishop does not control, with
  the defending king in reach of it.
- **Stalemate**: a lone king with no move and not in check scores 0 here, because
  quiescence search (which only looks at captures) would not notice.

In tests from scratch, the engine mates with queen and rook in every case and with
bishop and knight in most, at one second a move.

### Search

The search is **negamax alpha-beta** — one function serves both sides by
negating scores — with these parts, roughly in the order a node meets them:

**Iterative deepening and aspiration windows.** Depth 1, then 2, then 3 ... Each
iteration fills the transposition table and the move-ordering tables for the next,
which more than repays the repetition, and there is always a finished move when
the clock stops. From depth 4 on, the search first assumes the score will be near
the previous iteration's and uses a narrow window (±21 cp) around it, widening it
on each failure.

**Principal variation search.** The first move of a node is searched with the full
window; the others with a null window (only "is it better than the best so far?"),
and re-searched with the full window only if they are.

**Transposition table.** A hash table of buckets of four entries, each storing the
position's key, the best move, the score (exact, lower bound or upper bound), the
depth searched and the static evaluation, packed into two 64-bit words so threads
can share the table without locks (see [Threads](#threads)). A stored result deep
enough, with a score on the right side of the window, ends the node at once; its
move is tried first anyway. Entries from older searches and shallower depths are
replaced first.

**Draws and mates.** Repetitions (within the search, one repeat counts), the
fifty-move rule (unless the last move gave mate) and insufficient material score as
a draw, adjusted by contempt. Mate scores carry the distance to mate, so shorter
mates are preferred, and mate distance pruning skips nodes that cannot improve on a
mate already found.

**The static evaluation as the search sees it.** At each node the evaluation is
corrected by **correction history**: for every pawn structure (hashed) and side to
move, a running average of how far the search's result has differed from the
evaluation in the past. When a node's search finishes with a trustworthy score —
not in check, not decided by a capture, not a bound on the wrong side — the
difference is folded into the average, weighted by depth, and limited to ±256 cp.
When the transposition table holds a score that bounds the position on the right
side (a lower bound above the evaluation, or an upper bound below it), that score
is used instead for the pruning decisions below, as it says more.

**Pruning before the moves** (not in check, not on the principal variation):

- *Reverse futility* — up to depth 6, if the estimate exceeds beta by 89 cp per
  ply, the node returns at once.
- *Razoring* — up to depth 3, if the estimate is more than 246 cp per ply below
  alpha, quiescence search checks whether a capture can save the node, and if not
  it is dropped.
- *Null move* — the side to move passes; if a reduced search still beats beta,
  the node does too. The reduction is 2 + depth / 4 plies, plus one per 201 cp the
  estimate stands above beta (up to three). It is not used when the side to move
  has only pawns, where zugzwang makes it unsound.
- *Internal iterative reduction* — with no move from the table at depth 4 or
  more, the node is searched a ply shallower, since its ordering would be guesswork.

**Move ordering.** The earlier the best move is tried, the more of the tree is
cut, so moves are scored and picked best first (one at a time, since a cutoff
usually comes early): the table's move; promotions; captures that do not lose
material by static exchange evaluation, by most valuable victim and least valuable
attacker, ties broken by **capture history**; the two **killer moves** of the ply
(quiet moves that recently caused a cutoff there); the **counter move** (the quiet
move that last refuted the opponent's previous move); the remaining quiet moves by
**history** (how often a from-to move caused a cutoff) plus **continuation history**
(how well this piece-to-square move did after the opponent's last move, and after
our own move before that); and losing captures last. Every table is rewarded for
the move that causes a cutoff and penalised for the moves tried before it, with
updates that slow down as a score approaches its limit, so they stay bounded.

**Pruning and reductions in the move loop** (never the first move, never a move
that gives check):

- *Static exchange pruning* — up to depth 8, a capture losing more than 102 cp per
  ply, or a quiet move losing more than 39 cp times depth squared, once the
  exchange on its square is played out, is skipped.
- *Futility pruning* — at depth 1–3, if the estimate plus 19 + 100 cp per ply
  cannot reach alpha, quiet moves are skipped.
- *Late-move pruning* — at depth 1–3, quiet moves beyond 6, 10 or 16 are skipped.
- *Late move reductions* — moves late in the order are first searched shallower,
  by 0.79 + ln(depth) · ln(move number) / 2.15 plies, one ply less on the principal
  variation and for killers and the counter move, up to about three plies less or
  more by their history, and re-searched at full depth only if they beat alpha.

**Extensions.**

- *Check extension* — a node in check is searched a ply deeper.
- *Singular extension* — from depth 8, when the table holds a lower bound for its
  move from nearly as deep, the node is searched at half depth without that move,
  against a bar of the table score minus 2 cp per ply. If nothing else reaches the
  bar, the move is the only good one and is searched a ply deeper; if even the
  others beat beta, several moves refute the opponent's last move and the node
  returns at once (multi-cut).

**Quiescence search.** At depth zero the engine does not evaluate yet: the side to
move may "stand pat" on the static evaluation or try captures and promotions that
do not lose material, until the position is quiet. Captures that cannot bring the
score within 208 cp of alpha even if the piece were free are skipped (delta pruning). In check,
every evasion is searched, and having none is mate. Results go into the
transposition table at depth 0.

**Contempt.** A draw counts `Contempt` centipawns against the side the engine is
playing (and in its opponent's favour, so both sides of the tree agree). The engine
therefore only accepts a repetition when every alternative is worse by more than
that. Analysis uses no contempt, so a drawn position shows as 0.00.

### Time management

- **Fixed time per move** (`go movetime`, or a plain `go`, which means 500 ms): the
  engine keeps starting iterations until 90% of the time is gone and stops at the
  limit. An iteration cut short is not wasted: if it had already finished a root
  move with an exact score (or one that beat the window), that move is played
  instead of the previous iteration's.
- **On a clock** (`wtime`, `btime`, `winc`, `binc`, `movestogo`): the budget is the
  remaining time divided by the moves left (30 if not given) plus three quarters of
  the increment, at most a third of the clock. No new iteration starts past 60% of
  the budget, scaled by how settled the search is: 1.5× right after the best move
  changed, 1.2×, 1.0× and 0.85× as it survives more iterations, 0.75× after four,
  and 1.3× more when the score dropped by over 30 cp. An iteration is abandoned at
  2.5× the budget (at most a third of the clock); the partial-iteration rule above
  applies here too.
- With a single legal move the engine answers at once; with `go ponder` no limit
  applies until `ponderhit`, which starts the clock from that moment.

### Threads

Several threads search the same position at once (**Lazy SMP**). Each has its own
copy of the board and its own killer, counter-move, history, continuation, capture
and correction tables; they share only the transposition table and never wait for
each other. Thread 0 reports to the GUI and its move is played; the helpers skip
depths on their own patterns, so at any moment they are spread over several depths,
and everything they find reaches thread 0 through the table — deeper stored
results, more cutoffs. The reported depth therefore barely rises with more threads,
but the search becomes wider and parts of it deeper, which is what the strength
gain comes from.

The shared table is safe without locks: each entry is two 64-bit words, the data
and the key xored with the data. A reader that catches one thread's key beside
another's data gets a key that does not match, and treats it as a miss — a wasted
lookup, never a wrong answer. Buckets are 64 bytes, one cache line each. Node counts
are summed across threads for `info` and `go nodes`. With more than one thread a
search is not reproducible move for move.

### Tuning the evaluation (Texel)

The tuner (`make tuner`) is the engine built with an **evaluation trace**: every
time a weight is used, the trace records it, so for each position the evaluation is
known as a sum of *coefficient × weight* (the few non-linear parts held fixed). The
tuner reads positions labelled with game results, checks that this linear model
reproduces the real evaluation, finds the scale K that best turns the evaluation
into an expected score, 1 / (1 + 10^(−K · eval / 400)), and then lowers the mean
squared error between expected scores and results with Adam over all positions at
once, a tenth held out to check that the fit generalises. A light pull towards the
starting values keeps rarely seen weights from being fitted to a handful of games,
and the knight, bishop, rook and queen tables are tied to stay mirror-symmetric.
The result is written as C++ definitions and put into `eval.cpp` with
`apply_tuned.py`.

### Tuning the search (SPSA)

Sixteen numbers steer the search's pruning, reductions and extensions (the
futility, reverse-futility and razoring margins, the null-move reduction, the
late-move-reduction formula, the static exchange margins, the singular-extension
depth and margin, the aspiration window and the delta-pruning margin). Each is a
`SEARCH_PARAM` in `search.cpp`: a constant in the engine, and a UCI option in the
SPSA build (`make spsa`). The match runner's SPSA mode tunes them the way Fishtest
does: the engine plays itself in pairs of games, with every parameter nudged up for
one side and down for the other in random directions, and each pair's result moves
the parameters towards the side that did better. `spsa_params.txt` lists the
parameters with their ranges and step sizes, and `apply_spsa.py` writes tuned values
back into `search.cpp`. The values in `search.cpp` come from a 10,000-game run at
2 s + 0.02 s per game; against the starting values they scored +28 ± 29 Elo over
250 games at 8 s + 0.08 s.

## Files

| File | Contents |
|---|---|
| `types.h` | Colours, piece types, pieces, squares, the 16-bit move, scores and mate values |
| `bitboard.h/.cpp` | Bitboard operations, attack tables for every piece, magic bitboard generation |
| `position.h/.cpp` | The board, FEN reading and writing, Zobrist hashing, make/unmake, legality, repetition and draw rules, static exchange evaluation |
| `movegen.h/.cpp` | Pseudo-legal and legal move generation |
| `bitbase.h/.cpp` | King and pawn against king, solved at start-up |
| `eval.h/.cpp` | The evaluation, the known endings, and the tuning trace (tuner build only) |
| `search.h/.cpp` | Transposition table, move ordering, alpha-beta, quiescence search, time management, threads, search parameters |
| `perft.h/.cpp` | Perft counting and the correctness suite |
| `uci.cpp` | The UCI protocol, the benchmark and `main()` |
| `tune.cpp` | The Texel tuner and `explain` (tuner build only) |
| `apply_tuned.py` | Writes the tuner's weights into `eval.cpp` |
| `spsa_params.txt` | The search parameters for SPSA: start, range and step |
| `apply_spsa.py` | Writes SPSA-tuned parameters into `search.cpp` |
| `Makefile` | `engine`, `test`, `bench`, `tuner`, `spsa`, `clean` |

## Function reference

Every function, file by file. The source carries the same explanations as
comments.

### `types.h` — squares, pieces, moves, scores

| Function | What it does |
|---|---|
| `operator~(Color)` | The other colour, as a single bit flip. |
| `make_piece(c, pt)` | A piece from a colour and a type. Pieces are numbered `colour × 6 + type`, so this is arithmetic, not a lookup. |
| `type_of(p)` / `color_of(p)` | The reverse: the remainder is the type, the quotient the colour. |
| `rank_of(s)` / `file_of(s)` | A square's rank (square ÷ 8) and file (square mod 8). |
| `make_square(f, r)` | A square from a file and a rank. |
| `flip_rank(s)` | The square mirrored top to bottom (a1 ↔ a8), by xoring with 56. |
| `relative_square(c, s)` / `relative_rank(c, s)` | A square or rank as seen from one colour's side of the board, so one piece of code serves both colours. |
| `square_name(s)` | "e4" for a square, "-" for none. |
| `Move::Move()` | The empty move: all sixteen bits zero. |
| `Move::Move(uint16_t)` | A move rebuilt from its packed form, as the transposition table stores it. |
| `Move::Move(from, to, type)` | Packs origin (bits 0–5), destination (bits 6–11) and move type (bits 12–15) into sixteen bits. |
| `Move::from()` / `to()` / `type()` | Unpacks the three fields. |
| `Move::is_capture()` / `is_promotion()` | One bit test each: the move types are numbered so bit 2 means capture and bit 3 promotion. |
| `Move::is_castle()` / `is_en_passant()` | Comparisons with the two special move types. |
| `Move::promotion_type()` | Knight, bishop, rook or queen, from the low two bits of the move type. |
| `Move::is_none()` / `raw()` | Whether this is the empty move; the packed sixteen bits. |
| `Move::operator==` / `!=` | Compares the packed forms, so two moves are equal exactly when every field is. |
| `Move::to_uci()` | Long algebraic text ("e2e4", "e7e8q"), the format UCI uses. |
| `mate_in(ply)` / `mated_in(ply)` | Mate scores that include the distance, so a mate in three is preferred to a mate in five. |

### `bitboard.h` / `bitboard.cpp` — the board as 64-bit words

| Function | What it does |
|---|---|
| `square_bb(s)` | A bitboard holding just that square. |
| `test_bit` / `set_bit` / `clear_bit` | Tests, adds or removes one square. |
| `popcount(b)` | How many squares are in the set; one CPU instruction. |
| `lsb(b)` / `msb(b)` | The lowest and the highest square in the set. |
| `pop_lsb(b)` | Removes and returns the lowest square — the usual way to loop over a set of pieces. |
| `north` / `south` / `east` / `west` and the four diagonals | Moves every square of a set one step; the files a piece would wrap around from are masked off first. |
| `pawn_push<C>(b)` | Pushes every pawn of a colour one square forward at once. |
| `pawn_attacks_left<C>` / `pawn_attacks_right<C>` | The two capture directions, for every pawn of a colour at once. |
| `push_delta(c)` | One square forward as an offset: +8 for White, −8 for Black. |
| `pawn_attacks(c, s)` / `knight_attacks(s)` / `king_attacks(s)` | The precomputed attack sets of the pieces nothing can block. |
| `rook_attacks(s, occ)` / `bishop_attacks(s, occ)` | Magic lookup: mask the blockers that matter, multiply by the square's magic number, shift, read the table. |
| `queen_attacks(s, occ)` | A rook's and a bishop's attacks from the same square. |
| `attacks_of(pt, s, occ)` | The attacks of a knight, bishop, rook, queen or king, chosen at run time (pawns are left out: their attacks depend on colour). |
| `Magic::index(occ)` | The multiply-and-shift that turns a blocker configuration into a table slot. |
| `between_bb(a, b)` / `line_bb(a, b)` | The squares strictly between two squares, and the whole line through them; empty when they are not on a common line. |
| `aligned(a, b, c)` | Whether three squares share a rank, file or diagonal. |
| `sliding_attacks(sq, occ, dirs)` | The slow, plainly correct ray walk; only used to build the magic tables. |
| `relevant_occupancy(sq, dirs)` | The squares whose occupancy changes a slider's attacks (the board edge excluded: a blocker there blocks nothing). |
| `subset_of(index, mask)` | The *n*-th subset of a mask, spreading the bits of *n* across the mask's set bits, to enumerate every blocker configuration. |
| `Random::next()` / `sparse()` | A fixed-seed xorshift generator, and a variant that ANDs three numbers for the few set bits a good magic needs. |
| `init_magics(dirs, magics, table)` | Searches each square for a multiplier mapping every blocker configuration to a distinct slot (or a slot with the same attacks), and fills the table. |
| `Bitboards::init()` | Builds the pawn, knight and king tables, the magic tables for rooks and bishops, and the between/line tables. Called once at start-up. |
| `print_bitboard(b)` | Prints a bitboard as an 8×8 grid, for debugging. |

### `position.h` / `position.cpp` — the board and the rules

| Function | What it does |
|---|---|
| `Zobrist::init()` | Fills the hashing tables with fixed random numbers: per piece per square, Black to move, per castling combination, per en passant file. |
| `Position::set_start_position()` | The initial position, set up through the FEN reader so only one piece of code builds positions. |
| `Position::set_from_fen(fen)` | Reads a FEN. It refuses — leaving the position as it was — a malformed board, a missing or extra king, a pawn on the first or last rank, or the side that just moved left in check; castling rights and the en passant square survive only when the pieces they need are there and (for en passant) a capture is legal. |
| `Position::fen()` | Writes the position as a FEN; the inverse of the above. |
| `Position::compute_key()` | The Zobrist key built from scratch, for a newly set-up position; afterwards `make_move` keeps it current. |
| `Position::pieces(...)` | The board as bitboards: all pieces, one colour, one type, or one colour's pieces of one or two types. |
| `Position::piece_on(s)` / `empty(s)` | The piece array: what stands on a square, in one load. |
| `Position::king_square(c)` | Where a colour's king stands. |
| `Position::side_to_move()`, `ep_square()`, `castling_rights()`, `halfmove_clock()`, `fullmove_number()`, `key()`, `ply_from_root()` | The rest of what a FEN records, plus the hash key and how many moves have been made since the search's root. |
| `Position::attackers_to(s, occ)` | Every piece of either colour attacking a square, for a given occupancy. For pawns it reads the attack table backwards: a white pawn attacks *s* exactly where a black pawn on *s* would attack. |
| `Position::attacked_by(s, by, ...)` | Whether a colour attacks a square, stopping at the first attacker; an overload ignores a piece about to be captured. |
| `Position::in_check()` / `checkers()` | Whether the side to move is in check, and which pieces give it. |
| `Position::is_legal(m)` | Whether a pseudo-legal move leaves the own king safe, by building the occupancy it would leave and testing the king's square once — no move is made. |
| `Position::make_move(m)` | Plays a move: moves the pieces, updates the key term by term, and pushes a record of what the move destroyed (captured piece, castling rights, en passant square, clock, key). The en passant square after a double push is kept only if a capture onto it is legal. |
| `Position::can_capture_en_passant(ep)` | Whether the side to move has a legal en passant capture onto `ep`; only then is the square part of the position and its key. |
| `Position::unmake_move(m)` | Takes the move back by popping the record; nothing is recomputed. |
| `Position::make_null_move()` / `unmake_null_move()` | Passes the move to the opponent without moving anything (for null move pruning), and restores it. Resets the count of moves since the last pass, so repetition detection never looks through it. |
| `Position::is_repetition(ply)` | Whether the position occurred before: only positions since the last capture or pawn move, and only every second one (same side to move), can match. |
| `Position::is_fifty_move_draw()` | A hundred half-moves without a capture or a pawn move. |
| `Position::is_insufficient_material()` | Bare kings, king and one minor piece, or bishops all on one colour: no mate possible. |
| `Position::has_non_pawn_material(c)` | Whether a colour has anything besides pawns and the king. |
| `Position::see(m)` | Static exchange evaluation: plays out every capture and recapture on the move's destination, cheapest piece first and including sliders revealed behind the pieces that move, and returns the material won or lost. |
| `Position::parse_uci_move(text)` | Turns "e2e4" into the generator's own move, which fills in the move type (capture, double push, castle, en passant). |
| `Position::to_string()` | The board as text with its FEN and key, for the `d` command. |
| `Position::put_piece` / `remove_piece` / `move_piece` | The only three ways the board changes; each keeps bitboards, occupancy and the piece array in step. |

### `movegen.h` / `movegen.cpp` — generating moves

| Function | What it does |
|---|---|
| `MoveList::add(m)` | Appends a move (its ordering score is filled in by the search). |
| `MoveList::clear` / `size` / `empty` / `operator[]` / `begin` / `end` | A fixed array of 256 moves, so no memory is ever allocated during a search. |
| `generate_pawn_moves<Us, Type>` | Pushes, double pushes, all four promotions (with and without capture), captures and en passant, each a few shifts over the whole pawn bitboard. |
| `generate_piece_moves<Us, Type>` | Knight, bishop, rook, queen and king moves: the attack set minus the own pieces, one move per square left (only captures when generating captures). |
| `generate_castling<Us>` | Castling, when the right exists, the squares between are empty, and the king's start, path and destination are not attacked. |
| `generate_all<Us, Type>` | Every move of one colour: pawns, pieces, then castling (left out when generating captures only). |
| `generate_pseudo_legal(pos, list, type)` | Chooses the colour and the kind (all moves or captures) once, so neither is tested inside the loops. |
| `generate_legal(pos, list)` | Pseudo-legal moves filtered by `is_legal`. Used at the root, by perft, by the move parser and by the stalemate check; the search filters lazily instead. |

### `bitbase.h` / `bitbase.cpp` — king and pawn against king

| Function | What it does |
|---|---|
| `Bitbase::init()` | Classifies every KPK position by retrograde analysis and stores one bit per position (set: the side with the pawn wins). Called from `Eval::init()`. |
| `Bitbase::kpk_win(strong, king, pawn, weak_king, stm)` | Whether the side with the pawn wins: mirrors the position so that side is White and the pawn stands on files a–d, then reads the bit. |
| `index(stm, black_king, white_king, pawn)` | Packs a position into 18 bits: both kings, the side to move, the pawn's file (a–d) and rank. |
| `distance(a, b)` | King distance between two squares. |
| `Entry::Entry(idx)` | What a position shows by itself: impossible (kings touching, a king on the pawn, Black in check with White to move), a win (safe promotion), a draw (stalemate or the pawn captured), or unknown. |
| `Entry::classify(db)` | One step of the analysis: with White to move, a win if any move wins; with Black to move, a draw if any move draws; otherwise the other result once no move is unknown. |

### `eval.h` / `eval.cpp` — the evaluation

| Function | What it does |
|---|---|
| `Eval::init()` | Solves KPK (`Bitbase::init`), builds the combined material-plus-square tables, the pawn masks (passed, attack span, rear span), the outpost zones and the king-distance table. |
| `Eval::clear()` | Empties the calling thread's pawn hash table. |
| `Eval::set_fifty_move_scaling(on)` | Turns the fifty-move fade on or off. |
| `Eval::evaluate(pos)` | The evaluation with the fifty-move fade: `fifty_move_scale(evaluate_unscaled(pos), pos)`. |
| `Eval::evaluate_unscaled(pos)` | The evaluation before the fade — what the transposition table stores, since its key does not include the clock. |
| `Eval::fifty_move_scale(score, pos)` | `score × (200 − clock) / 200`, or the score unchanged when the fade is off. |
| `Score::add` / `sub` / `operator+=` | Accumulates the middlegame and endgame halves of a term together. |
| `push_to_edge(s)` | For mating a lone king: 20 in the centre rising steeply to 100 in a corner. |
| `push_close(d)` | For mating: worth most with one square between the kings. |
| `steps(a, b)` | File plus rank distance; unlike the king distance it falls with every step along an edge, which the bishop-and-knight corner needs. |
| `compute_pawn_structure<Us>(pos, entry)` | Doubled, isolated, backward and connected pawns and the passed pawns of one colour, with that colour's pawn attacks, written into a pawn table entry. |
| `pawn_entry(pos)` | The position's pawn table entry, computed on a miss; both pawn bitboards are stored and compared, so an entry is used only for the exact structure it describes. |
| `Evaluation::Evaluation(pos)` | Starts an evaluation by fetching the pawn structure; everything else happens in `value()`. |
| `Evaluation::initialize<Us>()` | One colour's pawn and king attack maps, its king zone, and its mobility area (squares not attacked by enemy pawns, not the own king, and not an own pawn that is blocked or still on its second or third rank). |
| `Evaluation::is_outpost<Us>(s)` | Whether a square is in the enemy half, defended by an own pawn and out of reach of every enemy pawn for good. |
| `Evaluation::pieces<Us>()` | Walks every knight, bishop, rook and queen: mobility, outposts, minor piece behind a pawn and king distance, bad and long-diagonal bishops, rook files, the seventh rank, trapped rooks and the bishop pair, filling the attack maps and counting the attackers on the enemy king on the way. |
| `Evaluation::king_shelter<Us>()` | The pawn shield in front of the own king, open and half-open files beside it, and how close the nearest enemy pawn on each has come (pawn storm). Middlegame only. |
| `Evaluation::king_safety<Us>()` | The shelter, the squares each enemy piece type could check from safely, and — from two attackers up — the attack units of the enemy pieces, squared, capped and scaled by the number of attackers, plus units for the king's ring squares not defended twice. |
| `Evaluation::threats<Us>()` | Enemy pieces attacked by pawns, rooks and queens attacked by minor pieces, the queen attacked by a rook, and enemy pieces attacked and undefended. |
| `Evaluation::passed_pawns<Us>()` | The endgame part of passed pawns: king distances, a free and a safe path, a blockader, rooks behind, and the rule of the square. |
| `Evaluation::material_and_psqt(score)` | Material and piece-square values of every piece in one sweep (White positive, Black negative), returning the game phase counted on the way. |
| `Evaluation::non_pawn_material(c)` | The middlegame value of a colour's pieces other than pawns and the king. |
| `Evaluation::opposite_bishops()` | Whether each side has exactly one bishop and they travel on different colours. |
| `Evaluation::scale_factor(eg)` | How much of the endgame score to believe (out of 64): reduced for opposite bishops, a pawnless side with a small edge, knights alone, and a defending king blocking the pawn in rook-and-pawn against rook or bishop-and-pawn against bishop. |
| `Evaluation::known_ending(white_score)` | Recognises the lone-king endings — KPK from the table, rook-pawn and wrong-bishop draws, bishop and knight, other mating material — and stalemate, and scores them directly; false for every other position. |
| `Evaluation::value()` | Known endings first (not in the tuner build); then every term in dependency order, the endgame scale, the blend by phase, and the result from the side to move's point of view plus tempo. In the tuner build it also keeps the totals before blending. |
| `TRACE(index, colour, count)` | Records that a weight was used `count` times for a colour. Empty in the engine; fills the trace in the tuner build. |
| `Eval::Tune::parameters()` | Tuner build: every tunable weight with its name, middlegame and endgame value, and which halves may move. |
| `Eval::Tune::trace(pos, out)` | Tuner build: evaluates a position and returns every weight's White-minus-Black count, the totals before blending, the phase, the scale and the tempo. |
| `Eval::Tune::source(mg, eg)` | Tuner build: the weights as C++ definitions in `eval.cpp`'s own form, each piece-square table's average moved into material. |
| `collect(params)` / `array_line` / `board` / `round_int` | Tuner build helpers: list the weights with their current values, and print arrays and 8×8 tables as source. |

### `search.h` / `search.cpp` — finding the move

| Function | What it does |
|---|---|
| `Search::init(mb)` | Allocates the transposition table at the largest power-of-two number of buckets that fits in `mb`, halving the request if the memory is not there. |
| `Search::hash_megabytes()` | The table size actually in use. |
| `Search::clear()` | Forgets everything learned: the table, the pawn cache, and every thread's killers, counter moves and history, continuation, capture and correction tables. |
| `Search::set_threads(count)` / `threads()` | How many threads search; the per-thread searchers are kept between moves so their tables carry over. |
| `Search::set_move_overhead(ms)` | Time kept back from every move. |
| `Search::set_contempt(cp)` / `contempt()` | How much a draw counts against the side the engine plays. |
| `Search::think(pos, limits)` | The whole search: prepare every thread, start the helpers, run thread 0, stop and join the helpers, and answer with thread 0's move, filling in a move to ponder on from the table if the line was cut short. |
| `Search::request_stop` / `clear_stop` / `stop_requested` | The stop flag set by the UCI thread and polled by the search. |
| `Search::set_pondering` / `ponderhit` / `pondering` | While pondering no time limit applies; `ponderhit` restarts the clock and ends pondering. |
| `Search::print_line(text)` | Writes one line to the GUI under a lock, so threads never interleave output. |
| `Search::tunables()` / `apply_tunables()` | SPSA build only: the list of search parameters exposed as UCI options, and the rebuild of the reduction table after one changes. |
| `pack(data)` / `unpack(word)` | Squeezes an entry's move, score, evaluation, depth, bound and age into one 64-bit word and back. |
| `tt_read(entry, key, data)` | Reads both words of an entry and xors them back together; false when empty or caught half-written. |
| `replacement_value(data)` | How readily an entry is overwritten: its depth minus 8 per search it is out of date. |
| `tt_probe(key, hit, data)` | Finds a position in its bucket of four, or the entry a new result should replace. |
| `tt_store(...)` | Writes a result, keeping a known best move when the new result has none, and not letting a much shallower inexact result from the same search replace a deeper one. |
| `score_to_tt(score, ply)` / `score_from_tt(score, ply)` | Makes mate scores relative to the stored position and back, so a stored "mate in 3" stays right at another depth. |
| `tt_cutoff(entry, score, alpha, beta)` | Whether a stored score ends the node: exact, or a bound on the right side of the window. |
| `register_param(...)` / `SEARCH_PARAM(...)` | Declares a search parameter: a constant in the engine, a variable registered as a UCI option in the SPSA build. |
| `init_reductions()` | Fills the late-move-reduction table from `LMR_BASE` and `LMR_DIVISOR`. |
| `now_ms()` | Milliseconds on a steady clock. |
| `Searcher::prepare(...)` | Readies one thread for a search: its copy of the position, counters cleared, contempt chosen, history halved rather than wiped. |
| `Searcher::clear_heuristics()` | Wipes one thread's killers, counter moves and history, continuation, capture and correction tables. |
| `Searcher::elapsed()` / `time_used()` | Milliseconds since the search began, and since the clock for the limits started (they differ after `ponderhit`). |
| `total_nodes()` | Nodes searched by all threads together, from the counts each republishes every 2048 nodes. |
| `draw_value(s, side)` | What a draw is worth at a node: contempt against the side the engine plays, in its opponent's favour; 0 when analysing. |
| `update_history(score, bonus)` | Moves a history score towards its limit, adding less the closer it already is, so scores stay bounded and old information fades (also for the 16-bit tables). |
| `continuation_entry(s, ply, back, piece, to)` | The continuation-history entry for playing `piece` to `to` after the move `back` plies earlier (1: the opponent's, 2: our own), or none after a null move or before the root. |
| `update_quiet_histories(s, ply, us, m, piece, bonus)` | Rewards (or penalises) a quiet move in the history and both continuation entries. |
| `continuation_score(s, ply, piece, to)` | The sum of a quiet move's two continuation-history entries. |
| `captured_type(pos, m)` | The type of piece a capture takes (a pawn for en passant). |
| `correction_index(pos)` | The correction-history entry of a position: its two pawn bitboards, mixed into an index. |
| `correction_value(s, pos)` | The correction for a position, in centipawns. |
| `corrected_eval(s, pos, raw_eval)` | The evaluation the search uses: faded by the fifty-move clock, plus the correction (known wins and mates left alone). |
| `update_correction(s, pos, score, raw_eval, depth)` | Moves a structure's correction towards the difference between the search's result and the evaluation, weighted by depth, limited to ±256 cp. |
| `counter_move(s, ply)` | The quiet move that last refuted the opponent's previous move. |
| `mvv_lva(pos, m)` | Most valuable victim, least valuable attacker: PxQ before QxP. |
| `score_moves(s, pos, list, tt_move, ply)` | Gives every move its ordering score (table move, promotions, winning captures with capture history, killers, counter move, quiet moves by history and continuation history, losing captures). |
| `pick_move(list, i)` | Selection sort one move at a time, so an early cutoff leaves the rest unsorted. |
| `check_time(s)` | Every 2048 nodes, publishes the node count and stops on a stop request, the main thread finishing, or the hard time limit; stops at once on the node limit — never before depth 1 has a move, never while pondering. |
| `qsearch(s, pos, alpha, beta, ply)` | Quiescence search: stand pat on the corrected evaluation or try non-losing captures and promotions (with delta pruning), every evasion in check, table probes and stores at depth 0. |
| `negamax(s, pos, depth, alpha, beta, ply, allow_null)` | The main search: draws, mate distance pruning, check extension, table probe and cutoff, corrected evaluation and table-refined estimate, reverse futility, razoring, null move, internal iterative reduction, then the move loop with static exchange pruning, singular extension, futility and late-move pruning, late move reductions and principal variation search, the cutoff updates of every ordering table, the correction-history update and the table store. Called with an excluded move, it answers the singular-extension question instead. |
| `pv_string(s)` | The expected line as UCI moves. |
| `score_to_uci(score)` | A score as UCI writes it: `cp` or `mate` in moves. |
| `hashfull()` | How full the table is, per mille, from its first thousand entries. |
| `report(s, depth, score, bound, pv)` | One `info` line with depth, selective depth, score, nodes, time, speed, hash use and line. |
| `set_time_limits(s, pos, limits)` | Turns `go`'s limits into a soft limit (no new iteration past it) and a hard limit (stop), for a fixed move time or a clock. |
| `allocate_tt(mb)` | Resizes the table. |
| `iterate(s, limits)` | One thread's iterative deepening with aspiration windows: records the best move found so far within an iteration, keeps it if the iteration is cut off, scales the soft limit by the stability of the best move and the score, and reports each finished depth (thread 0). Helper threads skip depths on their own pattern. |

### `perft.h` / `perft.cpp` — proving the move generator

| Function | What it does |
|---|---|
| `perft(pos, depth)` | Counts the leaves of the legal move tree to a depth, playing every legal move; no evaluation, no pruning. |
| `perft_divide(pos, depth)` | The same count split by first move, for finding a bug by comparison with a trusted engine. |
| `run_perft_suite()` | Seven standard positions against their published counts, about 17 million nodes. |

### `uci.cpp` — talking to the GUI

| Function | What it does |
|---|---|
| `wait_for_search()` / `stop_search()` | Waits for the search thread, after a stop request in the second case; every path that ends a search goes through them. |
| `to_lower(text)` / `trim(text)` | Case folding for option names and whitespace trimming for values. |
| `parse_int(token, value)` | Parses a whole token as an integer, rejecting leftovers. |
| `is_command(token)` / `is_go_keyword(token)` | The words the engine answers to (so unknown leading words can be skipped), and the words that start a `go` parameter (so `searchmoves` knows where its list ends). |
| `cmd_position(pos, in)` | Builds the new position separately and adopts it only if the FEN is valid, so a bad command never leaves the engine with a broken board. |
| `cmd_go(pos, in)` | Parses every `go` parameter, applies the default move time to a plain `go`, and starts the search on its own thread with its own copy of the position. |
| `cmd_setoption(in)` | Handles every option (and, in the SPSA build, the search parameters), reporting bad values and unknown options as `info string`. |
| `cmd_bench(depth)` | Searches a fixed set of positions to a fixed depth with one thread and reports nodes and speed; the node count is exactly reproducible. |
| `bench_depth(text)` | The depth for `bench`, 9 by default. |
| `main(argc, argv)` | Start-up (attack tables, hashing, evaluation tables, KPK table, transposition table), the shell commands (`test`, `bench`, `perft`, and `tune`/`explain` in the tuner build), then the UCI loop, which keeps reading while a search runs so `stop`, `isready` and `ponderhit` work at any time. |

### `tune.cpp` — fitting the evaluation (tuner build only)

| Function | What it does |
|---|---|
| `Tune::run(argc, argv)` | `tuner tune positions.txt [epochs] [output] [regularisation]`: loads and traces the positions, fits K, runs Adam with a tenth held out, writes the best held-out weights and lists the largest changes. |
| `Tune::explain(fen)` | `tuner explain "<FEN>"`: one position's evaluation split into every weight's contribution, largest first. |
| `load(...)` | Reads `FEN;result` lines, traces each position into a flat array of coefficients, and checks the linear model against the real evaluation. |
| `evaluate(d, i, mg, eg)` | The model's evaluation of one position for a set of weights: a dot product plus the fixed parts. |
| `sigmoid(k, eval)` | The expected score for an evaluation. |
| `mean_error(...)` / `parallel(...)` | The mean squared error over all positions, computed on every core. |

## Testing and tuning

The repository's `tools/match-runner` plays engine matches from the command line —
the GUI's openings (each played from both sides), clocks, adjudication and a
sequential probability ratio test that stops once a change is clearly better or
not. Every change to this engine is measured that way before it is kept: one thread
per engine, 8 s + 0.08 s per game, and the SPRT on pairs of games.

**Evaluation (Texel).**

```sh
make tuner
match-runner --engine1 ./engine --engine2 ./engine --nodes 10000 --random-plies 4 \
    --games 12000 --concurrency 8 --option Threads=1 --datagen positions.txt
./tuner tune positions.txt 2000 tuned.txt 3e-9      # epochs, output, regularisation
python3 apply_tuned.py tuned.txt eval.cpp && make
./tuner explain "<FEN>"                             # inspect one evaluation
```

**Search (SPSA).**

```sh
make spsa
match-runner --engine1 ./engine-spsa --spsa spsa_params.txt --spsa-out tuned_params.txt \
    --games 10000 --tc 2+0.02 --concurrency 8 --option Threads=1
python3 apply_spsa.py tuned_params.txt search.cpp && make
```

`tools/finish-engine.sh` runs the whole sequence — build, a short SPSA check, the
SPSA run, applying the values, tuned against untuned, and a final match against a
reference engine — and logs each stage.

## Known limitations

- No neural-network evaluation: the evaluation is hand-built and tuned, and the
  king-danger curve, scale factors and tempo are still hand-picked.
- No endgame tablebases beyond king and pawn against king, and no opening book.
- Contempt is a fixed number: it does not grow when the engine is ahead or shrink
  when it is behind.
- With more than one thread the search is not reproducible, and the helpers differ
  only in their tables and the depths they skip.
- No MultiPV: one line is reported.
- Standard chess only, not Chess960.
