# Bitboard chess engine

A complete chess engine in C++17, built along the architecture described in
Alexander Ameye's write-up: magic bitboards for board representation, a
pseudo-legal generator with a legality filter, tapered evaluation, and alpha-beta
search with move ordering.

Move generation is verified against the standard perft suite. It speaks UCI, so
it plugs into any chess GUI.

## Engine 12 (`Engine_12`)

UCI name "Bitboard Engine 12". Based on `../Engine_11`. Search and the tuned
evaluation are unchanged; the engine now knows a number of endings outright
instead of evaluating them term by term.

1. **King and pawn against king, solved exactly** (`bitbase.cpp`). At start-up
   every KPK position -- 196,608 once the pawn is mirrored onto files a to d -- is
   classified by retrograde analysis: positions decided on the spot (a pawn that
   queens safely, stalemate, the pawn taken) first, then repeatedly every position
   whose moves all lead to known results, until nothing changes. It takes a few
   milliseconds and is stored as one bit per position. The evaluation scores a won
   KPK position as a known win and a drawn one as 0, so the engine never pushes a
   pawn into a draw or gives up a win.
2. **Mating material against a lone king.** With a queen, a rook, two bishops on
   different colours, or bishop and knight, the position is scored as a known win
   (10,000 plus material) and the score rises as the lone king is driven to the
   edge and the strong king comes close -- the shape of every mating pattern. Bishop
   and knight can only mate in a corner of the bishop's colour, so there the lone
   king is pulled steeply towards that corner, one step at a time along the edge.
3. **Known draws against a lone king.** Rook pawns with the defending king in
   front of them, and bishop and rook pawns where the bishop does not control the
   queening square and the defending king reaches it ("wrong bishop"), score 0.
4. **Stalemate.** A lone king with no move and not in check is stalemated; the
   evaluation checks for it in these endings, because quiescence search does not.
5. **Blocking king.** Rook and pawn against rook, and bishop and pawn against
   bishop with the defending king on a square the bishop cannot reach, are scaled
   right down when the defending king stands in the pawn's path.

The recognised endings are left out of the tuner build, which keeps tuning the
ordinary terms.

**Checks.** Textbook positions come out right (KPK wins and draws, including
Ke5 Pe6 v Ke8 with Black to move, which is a draw after ...Ke7; the wrong bishop;
rook pawns). Play-outs from scratch, the engine playing both sides at 1 s a move:
queen against king mates 3 of 3 in 7-8 moves, rook against king 5 of 5 in 10-22,
bishop and knight against king 5 of 6 in 28-41 (Engine 11: 4 of 6). Getting bishop
and knight to work took two fixes found by the play-outs: the stalemate check, and
a corner pull steep enough (320 per step) that the lone king is not left in the
centre. **Benchmark** 285,237 nodes; perft is identical.

**Testing.** Against Engine 11, 300 games at 8 s + 0.08 s (one thread, eight
games at a time, adjudication on): 50.8%, +6 ± 25 Elo -- no measurable change in
ordinary games, as expected: these endings are rare in 300 games, and many games
are adjudicated before they are reached. What the knowledge buys shows in the
endings themselves (the checks and play-outs above).

## Earlier: Engine 11 (`Engine_11`)

UCI name "Bitboard Engine 11". Based on `../Engine_10`. The search is untouched;
the evaluation's weights are no longer hand-picked but fitted to game results
(Texel tuning), and it has new terms. Together: about +160 Elo over Engine 10
(+121 for the tuning, +42 more for the new terms and a second tuning round).
Endgame knowledge followed in Engine 12.

### 1. Texel tuning

Every weight in `eval.cpp` used to be a guess. Engine 11 fits them to the
results of real games:

1. **Data.** The engine plays itself with the match runner's `--datagen` mode
   (`../tools/match-runner`): 12,000 games at 10,000 nodes per move from the 293
   openings plus four or five random moves (so the positions are varied and many
   are unbalanced, which is how material gets valued). Each quiet position -- not
   in check, the engine's move not a capture or promotion, at least eight plies
   past the opening -- is written with its game's result. Round 1 used Engine 10
   (785,354 positions); round 2 added 759,564 positions played by the tuned
   Engine 11, 1.54 million in all.
2. **Trace.** `make tuner` builds the engine with `EVAL_TRACE`, under which the
   evaluation records how often it used each of about 480 weight pairs
   (middlegame and endgame) for every position: material, all six piece-square
   tables, pawn structure, passed pawns, mobility, outposts, rook files, threats,
   king shelter and the terms below. The king-danger formula, endgame scale factor
   and tempo are not linear and stay fixed. With the trace, the evaluation for
   any weights is a dot product; the tuner checks that it reproduces the real
   evaluation (within 2 cp of rounding) before it starts.
3. **Fit.** The expected score `1 / (1 + 10^(-K * eval / 400))`, with K fitted to
   the starting weights, should match each game's result. Adam lowers the mean
   squared error over all positions at once, a tenth of them held out to check
   the fit generalises, and the best held-out weights are kept. Two refinements
   keep the values chess-like rather than noise-fitted:
   - a light pull towards the starting values (L2, 3e-9 per squared centipawn),
     which barely moves well-supported weights and keeps rarely seen ones (a
     knight in the corner, connected pawns on the seventh) from being fitted to a
     handful of games -- without it, connected pawns on the seventh came out at
     +817;
   - mirror-symmetric knight, bishop, rook and queen tables (a3 = h3), which
     halves their weights; pawns and kings keep both wings, because which side the
     king castles to matters for them.
4. **Apply.** The tuner writes the weights as C++ definitions (piece-square
   averages moved into material), and `apply_tuned.py` puts them into `eval.cpp`.

Round 1 cut the held-out error from 0.0765 to 0.0697; round 2, with the new
terms and twice the data, to 0.0674. The piece values now (middlegame / endgame)
are pawn 81 / 114, knight 384 / 339, bishop 384 / 384,
rook 588 / 615 and queen 1121 / 1216. One visible effect: the King's Indian main
line, which Engine 10 scored +0.66 for White, is now close to level.

### 2. New evaluation terms

All linear, so the tuner fits them with everything else (values after round 2):

- **Safe checks** -- for each enemy piece type, the squares it could give check
  from without being taken: −38 (knight), −15 (bishop), −29 (rook), −32 (queen)
  per square, middlegame.
- **Pawn storm** -- the enemy pawn closest to our king on its file or a
  neighbour's, by rank: −59 when it stands one rank from a back-rank king.
- **Long diagonal** -- a bishop seeing two of the four centre squares through
  anything but pawns (the fianchettoed bishop): +28 / +12.
- **Trapped rook** -- at most three moves and shut in on its wing by its own
  king: −17 / −41.
- **Bad bishop** -- per own pawn on the bishop's square colour: +3 / −11.
- **Minor behind pawn** -- a knight or bishop with its own pawn in front: +9 / +1.
- **King protector** -- knight and bishop distance from their king, a few
  centipawns per square.

Two other ideas were tried and dropped: a value per number of safe squares for
mobility (66 weights instead of 4; −6 ± 26 Elo, too noisy), and middlegame space
(it came out negative, so it measured something else).

### Testing

8 s + 0.08 s per game, one thread per engine, eight games at a time, from the
GUI's 293 openings, with adjudication; run with `../tools/match-runner`.

| Version | Against | Games | Score | Elo |
|---|---|---|---|---|
| Texel tuning, round 1 | Engine 10 | 400 | 66.8% | +121 ± 31 |
| + per-count mobility and the new terms | round 1 | 300 | 49.2% | −6 ± 26 (dropped) |
| + the new terms, round 2 tuning on 1.54M positions | round 1 | 300 | 56.0% | +42 ± 30 |

The last row changes two things at once (new terms and more, stronger data), so
its gain belongs to both. **Benchmark** 282,260 nodes; perft is identical.

### Tuning it again

    make tuner
    ../tools/match-runner/bin/Release/net10.0/match-runner --engine1 ./engine --engine2 ./engine \
        --nodes 10000 --random-plies 4 --games 12000 --concurrency 8 --option Threads=1 --datagen positions.txt
    ./tuner tune positions.txt 2000 tuned.txt 3e-9    # epochs, output, regularisation
    python3 apply_tuned.py tuned.txt eval.cpp && make
    ./tuner explain "<FEN>"                           # every weight's share of one evaluation

## Earlier: Engine 10 (`Engine_10`)

UCI name "Bitboard Engine 10". Based on `../Engine_9_Repetition`. The
evaluation is untouched; four standard search techniques the engine was missing,
each measured on its own against the step before it (see *Testing* below).

1. **Static exchange pruning in the main search.** Up to depth 8, away from the
   principal variation and out of check, a move that loses material once the
   exchange on its square is played out is skipped without being searched: a
   capture losing more than 100 cp per ply of depth, a quiet move losing more
   than 40 cp times depth squared (so at depth 3 a knight may still be offered,
   at depth 2 not). The first move is always searched. Before, only quiescence
   search skipped losing captures.
2. **Continuation history and capture history.** Quiet moves were ordered by
   one history table (from-square, to-square) that ignores what came before.
   Now each thread also keeps a continuation table indexed by an earlier move
   (piece, destination) and the reply (piece, destination), read with the
   opponent's last move and with our own move before it, so the engine learns
   follow-ups: "after ...Nf6, h3 works". Quiet moves are ordered by the plain
   history plus both continuation entries, and late move reductions use the same
   sum. Captures of equal victim and attacker are ordered by a capture history
   (piece, destination, captured piece). All of these are rewarded on a cutoff
   and penalised for the moves tried before it.
3. **Singular extensions.** From depth 8, when the transposition table holds a
   move with a lower-bound score searched at most three plies shallower, the node
   is searched again at half depth *without* that move, against a bar of the
   table score minus two centipawns per ply. If nothing else reaches it, the move
   is the only good one ("singular") and is searched a ply deeper, so forcing
   lines are not cut short. If even the other moves beat beta, several moves
   refute the opponent's last move and the node returns at once ("multi-cut").
   The search without the move neither stores in nor takes a cutoff from the
   table.
4. **Time management.** On a clock the budget per move is unchanged (a thirtieth
   of the remaining time plus three quarters of the increment, at most a third of
   the clock), but how much of it is used now depends on the search: the soft
   limit (no new iteration past it) is multiplied by 1.5 when the best move just
   changed, 1.2 / 1.0 / 0.85 after it survived one / two / three iterations and
   0.75 after four or more, and by another 1.3 when the score fell more than
   30 cp since the previous iteration. The hard limit (abandon the iteration) is
   up to 2.5 times the budget, never more than a third of the remaining time.
   With a fixed time per move (`go movetime`, or a plain `go`) nothing changes.

**Tried and dropped: "improving".** Comparing a node's static evaluation with
the one two plies up, and pruning and reducing harder when it had not improved
(the usual way), cost Elo here: −21 ± 21 over 620 games against Engine 9. A
milder version (no extra reduction, late-move pruning only loosened when
improving) was level: +1 ± 29 over 300 games. With an untuned evaluation the
comparison is probably too noisy to steer the pruning. Neither is in Engine 10.

**Benchmark.** 294,497 nodes (Engine 9: 328,850). Perft is identical. By step:
276,405 after 1, 267,176 after 2, 294,497 after 3 (4 changes nothing at a fixed
depth).

**Testing.** Each step was tested against the step before it at 8 s + 0.08 s
per game, one thread per engine, from the GUI's 293 openings, with an SPRT
(H0 0 Elo, H1 10 Elo, 5% error each way) that was stopped at a game cap: the
first tests ran four games at a time, the later ones eight at a time with a cap
of 300 games. A test that ended without a decision but with a non-negative result
was kept.

| Step | Against | Games | Score | Elo | Kept |
|---|---|---|---|---|---|
| Improving | Engine 9 | 620 | 46.9% | −21 ± 21 | no |
| 1. SEE pruning | Engine 9 | 620 | 51.1% | +8 ± 21 | yes |
| 2. Continuation / capture history | step 1 | 300 | 51.7% | +12 ± 30 | yes |
| 3. Singular extensions | step 2 | 300 | 55.0% | +35 ± 31 | yes |
| 4. Time management | step 3 | 300 | 58.2% | +57 ± 30 | yes |
| Improving, softened | step 4 | 300 | 50.2% | +1 ± 29 | no |
| **Engine 10 (final)** | **Engine 9** | **400** | **68.5%** | **+135 ± 28** | |

The final check was a fixed 400-game match of the finished Engine 10 against
Engine 9, eight games at a time at 8 s + 0.08 s: +203 =142 −55, no game lost on
time. The steps add up to more than their parts would suggest because each was
measured against a stronger predecessor in short, capped tests; the 400-game
match is the number to quote.

## Earlier: Engine 9: Repetition (`Engine_9_Repetition`)

UCI name "Bitboard Engine 9 (Repetition)". Based on `../Engine_8_Contempt`. No
new ideas: four fixes, three of which Engine 8's contempt made matter more.

1. **Repetitions are recognised however a position was reached.** Engines 3 to 8
   descend from Engine 1, not Engine 2, so they kept its en passant bug: the en
   passant square was stored and hashed after every double pawn push, even when
   no pawn could take. The same position then had two Zobrist keys -- after
   `1.e4 e5` it was `e367c19b...`, after `1.e4 e5 2.Nf3 Nc6 3.Ng1 Nb8` it was
   `4437...` -- so the transposition table missed transpositions and, worse, the
   repetition check missed repetitions. The engine walked into threefold
   repetitions it could not see, and the GUI then ended the game as a draw;
   contempt cannot steer away from a repetition the search does not recognise.
   Engine 2's fix is carried forward: `Position::can_capture_en_passant()` keeps
   the square, in `make_move()` and when reading a FEN, only when the side to
   move has a legal en passant capture -- which is also the rule for repetitions
   in chess. Both lines above now give `4437245627c23529`. Perft is unchanged,
   since no legal move is lost. A FEN written out shows `-` instead of a square
   no pawn can take on, as the FIDE rules and most GUIs do.
2. **Checkmate beats the fifty-move rule.** The search declared a draw as soon
   as the clock reached 100 half-moves, before asking whether the side to move
   was mated. Engine 8, with `7k/8/6K1/8/8/8/8/R7 w - - 99 80`, played `Ra1-b1`
   for a contempt-adjusted -0.25; Engine 9 plays `Ra8#` and reports `mate 1`.
   Only when the clock has run out and the side is in check are the legal
   moves generated, so it costs nothing elsewhere.
3. **The transposition table no longer mixes fifty-move clocks.** Engine 8 faded
   the evaluation as the fifty-move clock ran down, but the table stored that
   faded evaluation under a key that does not include the clock, so the same
   position reached with another clock reused the wrong number. The evaluation
   is now split: `Eval::evaluate_unscaled()` is what the table stores, and
   `Eval::fifty_move_scale()` applies the fade whenever it is read.
   `Eval::evaluate()` still returns the two combined.
4. **No contempt in analysis.** On `go infinite` the search uses contempt 0, so
   a drawn position is shown as 0.00 instead of -0.25 for whichever side the
   analysis started from. Games (plain `go`, time controls, `go ponder`) still
   use the `Contempt` option. The GUI's analysis panel uses `go infinite`.

**Benchmark.** 328,850 nodes with the default options, 354,832 with `Contempt 0`
and `Fifty Move Scaling` off (Engine 8: 310,597 and 343,523). The counts change
because positions that used to have two keys now share one, which changes what
the table holds and therefore the move ordering. Perft is identical.

**Still to measure.** Engine 8's forty-game test of contempt was played with the
repetition bug, so its numbers say little. Run Engine 9 against Engine 8, and
Engine 9 with `Contempt 0` against Engine 9 with 25, for several hundred games
each on the GUI's Match tab; its "how games ended" line now counts repetitions
and fifty-move draws separately.

## Earlier: Engine 8: Contempt (`Engine_8_Contempt`)

UCI name "Bitboard Engine 8 (Contempt)". Based on `../Engine_7_Threads`. Two
changes, both about one weakness: the engine used to settle for a draw whenever
it could not see anything better, because a draw scored exactly zero and nothing
reaches zero more reliably than repeating a position.

1. **Contempt.** A draw is now scored as `Contempt` centipawns *against*
   whichever side started the search, and in its opponent's favour, so both
   sides' scores agree about who dislikes it. With the default of 25 the engine
   only repeats when every alternative is worse than a quarter of a pawn; in a
   level position it keeps playing. The sign follows the side to move at each
   node, because negamax flips the score every ply. `Contempt 0` restores the
   behaviour of every earlier version exactly.
2. **Fifty-move scaling.** The evaluation now fades towards zero as the
   fifty-move clock runs down -- `score * (200 - clock) / 200`, so a position two
   moves from a draw by rule is worth half what the same position is worth with a
   fresh clock. A side that shuffles watches its own advantage shrink, while a
   pawn move or a capture resets the clock and hands it straight back. That is
   the incentive to make progress instead of going round in circles.

Options: `Contempt` (0-100, default 25) and `Fifty Move Scaling` (on by
default). With contempt at 0 and scaling off, the benchmark is 343,523
positions -- identical to Engine 7, so neither change touched anything else.

**What the tests showed.** Forty games at 100 ms per move, both engines Engine 8,
so the option is the only difference:

| Contempt | Draws | by repetition | by the fifty-move rule |
|---|---|---|---|
| 0 both sides | 9 / 40 | 8 | 0 |
| 25 both sides | 10 / 40 | 4 | 4 |

Repetition draws roughly halved -- which is what the change was for -- but the
total did not fall: an engine that refuses to repeat shuffles on instead, until
the fifty-move rule ends the game anyway. Forty games is far too few to call
this measured (the margin is about four games either way); it is the shift in
*how* games end that shows the mechanism working.

**A third change that proved unnecessary.** An explicit root rule ("never walk
into a position that has occurred twice already") is redundant: a repetition one
ply from the root already returns a draw score, which contempt has made
negative, so the search declines it on its own and accepts it only when the
alternatives really are worse. Adding the rule as well would risk playing a
genuinely worse move to dodge a draw.

**One caveat.** Draw scores now depend on which side is to move at the root, and
the transposition table does not record that. In a game it does not arise -- an
engine is only ever asked to move for its own side, and `ucinewgame` clears the
table between games -- but an entry stored while analysing one side can be off by
up to `Contempt` when the other side is analysed. The value is small and entries
age out.

## Earlier: Engine 7: Threads (`Engine_7_Threads`)

UCI name "Bitboard Engine 7 (Threads)". Based on `../Engine_6_Evaluation`. The
search and the evaluation decide exactly as they did — with one thread the
benchmark node count is unchanged — but the search can now use several CPU cores
at once. Set the `Threads` option; the default is 1.

**Lazy SMP.** Every thread searches the whole position, from the root, with its
own history, killers, counter moves and copy of the board. They are not given
separate parts of the tree and never wait for each other. What they share is the
transposition table: a position one thread has already searched is one the
others get for free, and that is the entire cooperation. It is called "lazy"
because of what it leaves out, and it works because alpha-beta spends most of
its time re-reaching positions that are already known.

1. **Thread 0 is the one that counts.** Its move is the move played, and it is
   the only thread that reports to the GUI. The helpers exist to fill the table.
   When thread 0 finishes, a shared flag tells the helpers to stop, and they are
   joined before the move is sent.
2. **Helpers search at staggered depths.** Each skips depths on its own pattern,
   so at any moment the threads are spread across several depths instead of all
   repeating the same one. Together with their separate history tables, that is
   what makes them explore differently rather than duplicating work.
3. **A transposition table safe to share without locks.** Each entry is now two
   64-bit words: the payload (move, score, evaluation, depth, bound, age) packed
   into one, and the position key with that payload xored into it. Each word is
   written in one piece, but the pair is not, so a reader can catch the key of
   one store beside the payload of another — and the xor catches exactly that,
   because the key only comes back out when both words are from the same write.
   A mismatch reads as a miss, which costs a re-search and never a wrong answer.
   Buckets are aligned to 64 bytes, so each one is a single cache line and
   threads working on different buckets do not fight over the same line.
4. **A pawn hash table per thread.** Unlike the shared table, its entries are far
   too big to update in single words, so each thread gets its own — half a
   megabyte each.
5. **Node counts across threads.** Every thread republishes its count every 2048
   nodes; `info nodes`, `nps` and the `go nodes` limit count what all of them
   have searched between them.

Measured on an Apple M4 (4 performance cores), 3 seconds on one middlegame
position:

| Threads | Depth reached | Positions per second |
|---|---|---|
| 1 | 17 | 2.2M |
| 2 | 17 | 4.5M |
| 4 | 19 | 8.5M |
| 6 | 18 | 10.5M |

Six threads reaches a *lower* depth than four despite searching more positions:
the extra threads land on efficiency cores, which slows the four that matter.
Set `Threads` to the number of performance cores, not the total.

One consequence to be aware of: with more than one thread a search is no longer
reproducible. Threads interleave differently on every run, so the same position
and the same time limit can give a different move. With `Threads 1` the engine
is deterministic again, which is why `bench` is run that way.

## Earlier: Engine 6: Evaluation (`Engine_6_Evaluation`)

UCI name "Bitboard Engine 6 (Evaluation)". Based on `../Engine_5_UCI`. The
search is untouched; the static evaluation was rewritten around a set of attack
maps, which is what the new terms are built on, and every function in the engine
now carries a comment explaining it (see [Function reference](#function-reference)).

1. **Pawn hash table.** Pawn structure changes only when a pawn moves or is
   captured, but the evaluation is called millions of times per search, so the
   structural terms and the set of passed pawns are cached, keyed on the two
   pawn bitboards. An entry stores those bitboards and is used only when both
   match exactly, so a collision costs a recomputation and can never give a
   wrong answer. Terms that depend on pieces or kings are never cached.
2. **More pawn structure.**
   - *Backward pawns:* a pawn with no friendly pawn beside or behind it on the
     neighbouring files, whose next square is covered by an enemy pawn. It can
     neither advance nor be defended by a pawn.
   - *Connected pawns:* pawns defending each other or standing side by side,
     scored by rank; a pair side by side counts for half as much again, and each
     defender adds a little more.
3. **Passed pawns understood dynamically.** The rank bonus stays; on top of it,
   and only in the endgame:
   - *King distance:* how far each king is from the square in front of the pawn.
   - *Free and safe paths:* nothing in the way is worth more than a piece parked
     in front, and a path the opponent does not attack at all is worth more still.
   - *Rooks:* a friendly rook behind the pawn defends every square it advances
     to; an enemy rook there does the opposite.
   - *Rule of the square:* with no enemy pieces left, the engine counts whether
     their king reaches the queening square before the pawn does. If it cannot,
     the pawn is scored as nearly a queen.
4. **Outposts.** A knight or bishop on a square in the enemy half that one of
   our pawns defends and no enemy pawn can ever attack, because none is left on
   the neighbouring files ahead of it.
5. **Rooks on the seventh,** when there is something to attack there: the enemy
   king shut on its last rank, or enemy pawns still on the seventh.
6. **Threats.** What we attack that the opponent must answer: a pawn attacking a
   piece, a knight or bishop attacking a rook or queen, a rook attacking the
   queen, and any piece we attack that nothing defends.
7. **King safety: weak squares.** Squares in the ring around the king that the
   enemy attacks and we do not defend twice now add to the attack units. Those
   are the squares an attacking piece can land on and survive.
8. **Mobility measured against a baseline.** Each piece type has an average
   number of squares; only the difference from it scores, so an ordinary
   position is not inflated and a cramped piece is a real penalty. Squares
   holding our own stuck pawns (on the first two ranks, or blocked) no longer
   count as mobility.
9. **Drawish endgames are scaled down.** The endgame score is multiplied by a
   factor before the phases are blended:
   - bishops of opposite colours, which let the defender give the bishop for the
     last pawn;
   - a side with no pawns at all, which needs a clear rook's worth of extra
     material before a win exists;
   - knights without pawns, which cannot force mate however many there are.

## Earlier: Engine 5: UCI (`Engine_5_UCI`)

UCI name "Bitboard Engine 5 (UCI)". Based on `../Engine_4_Search`. Only the
protocol and practical handling changed (`uci.cpp`, `search.h/.cpp`, and FEN
loading in `position.cpp`); the search and evaluation make the same decisions,
so `bench` gives the same node count as Engine 4.

1. **Pondering.** `go ponder` thinks on the opponent's time with no time limit;
   `ponderhit` starts the clock from that moment, and `stop` ends it. The engine
   advertises the `Ponder` option, so GUIs that ponder will use it. If the best
   line was cut short, the ponder move is taken from the transposition table,
   so `bestmove` nearly always includes one.
2. **All of `go`.** New: `nodes`, `mate` (ends on a mate in that many moves or
   fewer) and `searchmoves` (only the listed root moves). Limits now combine,
   so `go depth 20 movetime 1000` ends at whichever comes first. Missing or
   invalid numbers no longer break the command, and unknown words are reported
   and skipped.
3. **Options.**
   - `Hash`: 1–4096 MB. If the memory isn't there, the size is halved until it
     fits and an `info string` says so, instead of crashing.
   - `Clear Hash`: a button that empties the table and history.
   - `Move Overhead`: 0–5000 ms (default 10) kept back from every move, for
     slow GUIs or connections.
   - Option names are case-insensitive and may contain spaces.
4. **Clock handling.** The move overhead is also kept back from `wtime/btime`
   time controls. `wtime 0` (or negative) now makes the engine move almost
   immediately; before, a clock at zero meant *no* limit at all. With only one
   legal move, a timed search answers after depth 1 instead of spending its
   time.
5. **More `info`.** Every depth reports `seldepth` (the deepest line reached)
   and `hashfull`. After 1 s, scores that fall outside the aspiration window are
   reported as `lowerbound`/`upperbound`. After 3 s, the root move being
   searched is shown (`currmove`, `currmovenumber`). A search that is stopped
   mid-depth ends with a line giving the final node count and time. With no
   legal move the engine reports `score mate 0` (mated) or `score cp 0`
   (stalemate) before `bestmove 0000`.
6. **Robust input.**
   - An invalid FEN is rejected and the previous position kept. A FEN is
     rejected for a malformed board, missing or extra kings, pawns on the first
     or last rank, or the side not to move being in check.
   - Castling rights without the king and rook on their squares, and en passant
     squares without the pawn that just double-pushed, are dropped. These
     previously corrupted the board.
   - A command arriving during `go infinite` (or an unanswered `go ponder`)
     stops that search, instead of the engine hanging while it waits for a
     search that never ends.
   - Unknown commands are answered with `info string`, and words before a
     known command are skipped, as UCI requires.
   - `debug` and `register` are accepted.
   - `bench` takes an optional depth (`bench 12`), and no longer replaces the
     position set with `position`.

## Earlier: Engine 4: Search (`Engine_4_Search`)

UCI name "Bitboard Engine 4 (Search)". Based on `../Engine_3_Highest_Value`.
Only the search changed (plus a null-move counter in `position.h/.cpp`):

1. **Repetitions don't reach back past a null move.** Null move pruning lets a
   side "pass". Engine 3's repetition check looked back through those passes,
   so a position that reappeared only because a side passed was scored as a
   draw. Each position now counts the plies since the last null move
   (`plies_from_null`), and the repetition check stops there.
2. **Transposition table.**
   - *Buckets of four entries.* A position can use any entry in its bucket, so
     new results rarely evict valuable ones. The entry to replace is chosen by
     depth minus 8 plies per search the entry is old: empty and stale entries go
     first, then the shallowest.
   - *Safer updates.* A clearly shallower, inexact result doesn't overwrite a
     deeper one for the same position from the same search, and a stored best
     move is kept when a new result has none.
   - *Static evaluation stored,* so a table hit saves recomputing it.
   - *Used by quiescence search too:* probed for cutoffs (outside the principal
     variation) and the best move, and stored at depth 0.
3. **More pruning at low depth.** None of it applies on the principal
   variation, in check, to moves that give check, near mate scores, or to the
   first move:
   - *Futility pruning:* at depth 1–3, if the static evaluation plus a margin
     (120 / 220 / 320 cp) can't reach alpha, quiet moves are skipped.
   - *Late-move pruning:* at depth 1–3, once 6 / 10 / 16 quiet moves have been
     searched, the remaining quiet moves are skipped.
   - *Internal iterative reduction:* at depth 4 or more with no move from the
     table, the node is searched one ply shallower; its ordering would be
     guesswork, and the next iteration will find a stored move.
4. **Move ordering.**
   - *Counter-move heuristic:* the quiet move that last refuted a move (by that
     move's piece and destination) is tried right after the killer moves.
   - *Bounded history:* updates use a "gravity" formula that keeps scores
     within ±16384, and quiet moves tried before a cutoff are penalised as much
     as the cutoff move is rewarded (previously scores grew without limit).
5. **Logarithmic late move reductions.** The reduction is
   `0.75 + ln(depth) · ln(move number) / 2.25` plies instead of a fixed 1–3,
   one ply less on the principal variation and for killer and counter moves,
   up to two plies less or more depending on the move's history, never for
   moves that give check, and always leaving at least one ply to search.

Also: the search returns before touching the principal variation tables when a
line reaches `MAX_PLY`.

## Earlier: Engine 3: Highest Value (`Engine_3_Highest_Value`)

UCI name "Bitboard Engine 3 (Highest Value)". Based on the original engine
(`../engine`, "Bitboard Engine 1.0"), with the five highest-value improvements.
It does not include the changes in `../engine_2` (en passant hashing) or
`../Engine_Search_Extension` (search extensions).

1. **`stop` works: the search runs on its own thread.** The UCI loop keeps
   reading commands while the engine thinks. `stop` ends the search at once,
   `isready` is answered immediately, and `quit` or the GUI closing its end of
   the pipe ends a search before the engine exits, so it can't be left running.
   Other commands wait for the search to finish, and all protocol output goes
   through one locked writer, so the two threads never share data or interleave
   lines. Depth 1 always completes, so even an immediate `stop` returns a real
   move, and `go infinite` never answers before `stop`.
2. **A plain `go` thinks by time, not to depth 8.** With no limits the engine
   now searches for `DEFAULT_MOVE_TIME_MS` (500 ms, in `uci.cpp`). Depth 8 took
   1 ms in some positions and seconds in others, and made matches between
   versions unfair when one searched more per depth. With a fixed move time
   (plain `go` or `go movetime`), the engine uses up to the whole budget but
   doesn't start a new depth after half of it: the next depth usually costs more
   than all earlier ones together, and an unfinished depth is thrown away.
   `go depth`, `go movetime` and `go wtime/btime` still work as before.
3. **Quiescence search handles check.** Previously a position in check at the
   end of the main search was scored by "standing pat" on the static evaluation,
   as if the side to move could simply pass, and only captures were tried, so
   mates were missed. Now every evasion is searched in check, and having none
   returns a mate score.
4. **Static exchange evaluation (SEE).** `Position::see` plays out all
   recaptures on a square, least valuable piece first and including pieces
   hidden behind others (x-rays), and returns the material won or lost. Captures
   that lose material are now searched after the quiet moves instead of before
   them, and quiescence skips them entirely.
5. **King safety in the evaluation.** Middlegame terms, faded out as material
   comes off:
   - *Attack:* each piece hitting the squares around the enemy king adds attack
     units (knight and bishop 2, rook 3, queen 5 per square); the bonus grows
     with the square of the units and counts in full only with several attackers.
   - *Shelter:* a penalty for each of the king's file and its neighbours without
     a pawn right in front of the king (smaller if the pawn is one square
     further up), and for those files being half-open or open.

## Build

```sh
make            # produces ./engine
make test       # runs the perft suite
make bench      # fixed-depth search benchmark
make tuner      # produces ./tuner: the engine plus the Texel tuner
```

Requires only a C++17 compiler. `-march=native` is on by default for the
hardware popcount and bit-scan instructions; remove it from the Makefile if you
need a portable binary.

## Run

```sh
./engine test          # perft correctness suite
./engine bench         # node-count benchmark
./engine perft 6       # per-move breakdown from the start position
./engine               # interactive UCI mode
```

In interactive mode, beyond the UCI commands there are a few conveniences:

```
position startpos moves e2e4 e7e5
d                      # print the board and FEN
eval                   # static evaluation of the current position
moves                  # list all legal moves
perft 5                # divide from the current position
go                     # think for the default 500 ms
go depth 12
go movetime 3000
go wtime 300000 btime 300000 winc 2000 binc 2000
go nodes 1000000
go mate 3              # stop on a mate in 3 or fewer
go searchmoves e2e4 d2d4
go infinite            # until "stop"
go ponder              # no time limit until "ponderhit" (then 500 ms) or "stop"
ponderhit
stop
setoption name Hash value 256
setoption name Move Overhead value 50
setoption name Clear Hash
bench 12               # benchmark at another depth (default 9)
```

To play against it, point a GUI (Cute Chess, Arena, BanksiaGUI, En Croissant) at
the `engine` binary as a UCI engine.

## Files

| File | Contents |
|---|---|
| `types.h` | Squares, pieces, colours, the 16-bit move encoding |
| `bitboard.h/.cpp` | Bitboard operations, attack tables, magic bitboard generation |
| `position.h/.cpp` | Board state, FEN, Zobrist hashing, make/unmake, attack queries |
| `movegen.h/.cpp` | Pseudo-legal and legal move generation |
| `bitbase.h/.cpp` | King and pawn against king, solved at start-up |
| `eval.h/.cpp` | Tapered evaluation and known endings, with the tuning trace (`EVAL_TRACE` builds only) |
| `tune.cpp` | The Texel tuner and `explain` (in the `tuner` binary only) |
| `apply_tuned.py` | Writes the tuner's output into `eval.cpp` |
| `search.h/.cpp` | Alpha-beta, transposition table, quiescence, time management |
| `perft.h/.cpp` | Node counting and the correctness suite |
| `uci.cpp` | Protocol handling and `main()` |

## How it works

### Board representation

Twelve bitboards, one per piece type per colour, plus per-colour and combined
occupancy, plus a redundant 64-entry piece array (a mailbox) so that "what is on
this square" is one load instead of twelve tests.

Squares use the little-endian rank-file mapping: A1 is bit 0, H8 is bit 63.
This makes `north` a shift left by 8 and `east` a shift left by 1, with a file
mask to stop pieces wrapping around the board edge.

### Attack generation

Leaping pieces (pawns, knights, kings) have no blockers to consider, so their
attack sets are precomputed once into simple lookup tables.

Sliding pieces do have blockers, and are handled with magic bitboards. For each
square, the squares whose occupancy actually matters are masked off (board edges
are excluded — a blocker on the last square cannot block anything behind it).
Every possible blocker configuration for that mask is enumerated and its attack
set computed by walking the rays. Then a multiplier is searched for that hashes
every configuration to a distinct table slot. At runtime, one multiply and one
shift replace the ray walk.

The magic constants are searched at startup rather than hardcoded, which takes a
few milliseconds and keeps the source self-contained.

### Move generation

Moves are generated pseudo-legally — they may leave the king in check — and
filtered afterwards. This is faster than generating only legal moves, because
most positions are not pinned and the filter is cheap.

The filter (`Position::is_legal`) does not make the move on the board. It
assembles the resulting occupancy directly and tests the king square once. That
handles pins, discovered checks and the awkward en-passant case (where two
pieces leave the same rank simultaneously) uniformly, in O(1).

Pawns get their own routine because they push, double-push, capture diagonally,
promote and capture en passant. Bitboards make this compact: one shift handles
every pawn of a colour at once.

### Correctness

`make test` runs perft on seven positions chosen to exercise castling rights, en
passant including the discovered-check case, all four promotion pieces, pinned
pieces and check evasion. Around 17 million nodes, all matching published
reference counts.

This matters more than it sounds. Move generation bugs are subtle — a
mishandled en-passant pin might affect one position in ten thousand — and they
corrupt search results invisibly. If you change anything in `movegen.cpp` or
`position.cpp`, run `make test` before trusting the result.

### Evaluation

Material plus piece-square tables, interpolated between a middlegame and an
endgame set according to how much material remains. A rook is worth more as the
board empties; a king wants shelter in the middlegame and the centre in the
endgame. One number cannot express both, so two are kept and blended.

On top of that, in the order the terms are computed:

- **Pawn structure** — doubled, isolated, backward, connected and passed pawns.
  Cached in the pawn hash table, since it depends on nothing but the pawns.
- **Pieces** — mobility against a per-piece baseline, knight and bishop
  outposts, minor pieces behind a pawn and their distance from the king, bishops
  blocked by their own pawns or on the long diagonal, rooks on open and semi-open
  files, on the seventh rank or trapped by their king, and the bishop pair. This pass also records every square each side attacks, and how
  many pieces bear on the enemy king.
- **King safety** — pawn shelter, open files and pawn storms beside the king,
  squares the enemy could give a safe check from, plus the attack units from the
  pass above, weighted by how many attackers join in and by the
  squares in the king's ring that we fail to defend twice.
- **Threats** — pieces attacked by pawns, rooks and queens attacked by minor
  pieces, the queen attacked by a rook, and anything attacked but undefended.
- **Passed pawns** — king distances, whether the path is clear and unattacked,
  rooks behind the pawn, and the rule of the square.
- **Scaling** — the endgame score is scaled down in endings that cannot be won:
  opposite-coloured bishops, no pawns with a small material edge, knights alone.
- **Tempo** — a small bonus for having the move.
- **Fifty-move scaling** — the whole score fades towards zero as the fifty-move
  clock runs down, so shuffling costs the better side its advantage.

Every weight except the king-danger formula, the scale factors and tempo is
fitted to game results (see *Texel tuning* above). Everything is kept as a
(middlegame, endgame) pair until the last step, and the
attack maps are why the order matters: threats and king safety cannot be scored
until both sides' pieces have been walked.

### Search

Negamax alpha-beta with:

- **Iterative deepening** — search depth 1, then 2, then 3. Each pass fills the
  transposition table and improves ordering for the next, which more than repays
  the repeated work, and guarantees a usable move whenever the clock stops.
- **Transposition table** — the same position arises via many move orders.
  Buckets of four entries store the depth searched, whether the score is exact
  or a bound, the best move and the static evaluation.
- **Move ordering** — TT move, then promotions, then captures that don't lose
  material by static exchange evaluation (by most-valuable-victim/
  least-valuable-attacker, ties broken by capture history), then killer moves,
  then the counter move, then quiet moves by history plus continuation history
  (what worked after the last two moves), and losing captures last. Ordering is what makes pruning
  effective: the earlier the best move is tried, the more of the tree gets cut.
- **Quiescence search** — at depth zero, keep searching captures that don't
  lose material until the position is quiet, and every evasion when in check,
  with transposition table probes and stores. Without this the engine evaluates
  positions mid-exchange and believes the material count (the horizon effect).
- **Low-depth pruning** — futility pruning, late-move pruning, static exchange
  pruning of moves that lose material, and internal iterative reduction.
- **Null move pruning** — give the opponent a free move; if the position still
  beats beta, the real move will too. Disabled when the side to move has only
  pawns, where zugzwang makes the assumption false.
- **Late move reductions** — moves ordered late are searched shallower first,
  by a logarithmic amount adjusted for history, killers and counter moves, and
  re-searched at full depth only if they beat alpha.
- **Aspiration windows** — search a narrow band around the previous iteration's
  score, widening on failure.
- **Check extensions**, **singular extensions** (the table's move searched a ply
  deeper when nothing else comes close, multi-cut when several moves beat beta)
  and **mate distance pruning**.
- **Time management** — on a clock, more time while the best move keeps
  changing or the score falls, less once the move is settled.
- **Lazy SMP** — several threads search the whole position at once, sharing
  only the transposition table.
- **Contempt** — a draw scores slightly against the side that is searching, so
  the engine plays on in level positions instead of repeating (except when
  analysing with `go infinite`, where a draw is shown as level).

## Function reference

Every function in the engine, file by file, and what it does. The source
carries the same explanations as comments.

### `types.h` — squares, pieces, moves, scores

| Function | What it does |
|---|---|
| `operator~(Color)` | The other colour, as a single bit flip. |
| `make_piece(c, pt)` | Builds a piece from a colour and a type. Pieces are numbered `colour * 6 + type`, so this is arithmetic, not a lookup. |
| `type_of(p)` / `color_of(p)` | The reverse: the remainder is the type, the quotient is the colour. |
| `rank_of(s)` / `file_of(s)` | The rank is the square number divided by eight, the file is the remainder. |
| `make_square(f, r)` | Puts a file and rank back together into a square. |
| `flip_rank(s)` | Mirrors a square top to bottom (a1 ↔ a8) by xoring with 56. |
| `relative_square(c, s)` / `relative_rank(c, s)` | The same square or rank seen from a colour's side of the board, so one piece of code can serve both colours. |
| `square_name(s)` | "e4" for a square, "-" for none. |
| `Move::Move()` | The empty move: all sixteen bits zero. |
| `Move::Move(uint16_t)` | Rebuilds a move from its packed form, which is how the transposition table stores one. |
| `Move::Move(from, to, type)` | Packs origin (bits 0–5), destination (bits 6–11) and move type (bits 12–15) into sixteen bits. |
| `Move::from()` / `to()` / `type()` | Unpacks those three fields again. |
| `Move::is_capture()` / `is_promotion()` | One bit test each: the move-type numbers were chosen so that bit 2 means capture and bit 3 means promotion. |
| `Move::is_castle()` / `is_en_passant()` | Comparisons against the two special move types. |
| `Move::promotion_type()` | Knight, bishop, rook or queen, read out of the low two bits of the move type. |
| `Move::is_none()` / `raw()` | Whether this is the empty move, and the packed sixteen bits. |
| `Move::operator==` / `!=` | Compares the packed forms, so two moves are equal exactly when every field is. |
| `Move::to_uci()` | Long algebraic text, "e2e4" or "e7e8q", which is the format UCI speaks. |
| `mate_in(ply)` / `mated_in(ply)` | Mate scores that include the distance, so that a mate in three is preferred to a mate in five. |

### `bitboard.h` / `bitboard.cpp` — the board as 64-bit words

| Function | What it does |
|---|---|
| `square_bb(s)` | A bitboard holding just that square. |
| `test_bit` / `set_bit` / `clear_bit` | Ask about, add or remove one square. |
| `popcount(b)` | How many squares are in the set; one CPU instruction. |
| `lsb(b)` / `msb(b)` | The lowest and highest square in the set. |
| `pop_lsb(b)` | Removes and returns the lowest square: the standard way to loop over a set of pieces. |
| `north` / `south` / `east` / `west` and the four diagonals | Move every square in a set one step. Files are masked off first, so a piece on the a-file cannot wrap around to the h-file. |
| `pawn_push<C>(b)` | Pushes every pawn of a colour one square forward at once. |
| `pawn_attacks_left<C>` / `pawn_attacks_right<C>` | The two capture directions, again for every pawn at once. |
| `push_delta(c)` | One square forward as a square-number offset: +8 or −8. |
| `pawn_attacks(c, s)` / `knight_attacks(s)` / `king_attacks(s)` | Attack sets of the leaping pieces: precomputed tables, since nothing can block them. |
| `rook_attacks(s, occ)` / `bishop_attacks(s, occ)` | Magic lookup: keep the blockers that matter, multiply by the square's magic number, shift, and read the answer from the table. |
| `queen_attacks(s, occ)` | A rook and a bishop on the same square. |
| `attacks_of(pt, s, occ)` | Whichever of the above the piece type needs, chosen at run time. Pawns are excluded because their attacks depend on colour. |
| `Magic::index(occ)` | The multiply-and-shift that turns a blocker configuration into a table slot. |
| `between_bb(a, b)` / `line_bb(a, b)` | The squares strictly between two squares, and the whole line through them; empty when they are not aligned. |
| `aligned(a, b, c)` | Whether three squares share a rank, file or diagonal. |
| `sliding_attacks(sq, occ, dirs)` | The slow, obviously correct ray walk. Used only to build the magic tables at start-up. |
| `relevant_occupancy(sq, dirs)` | The squares whose occupancy actually changes a slider's attacks. Board edges are excluded: a blocker on the far edge blocks nothing behind it. |
| `subset_of(index, mask)` | Enumerates the *n*-th subset of a mask, by spreading the bits of *n* across the mask's set bits. |
| `Random::next()` / `sparse()` | A fixed-seed xorshift generator, and a variant that ANDs three numbers together to get the roughly eight set bits a usable magic needs. |
| `init_magics(dirs, magics, table)` | Searches for a multiplier per square that maps every blocker configuration to a distinct slot, accepting collisions when both configurations give the same attacks. |
| `Bitboards::init()` | Builds the pawn, knight and king tables, runs the magic search for rooks and bishops, and fills the alignment tables. Call once at start-up. |
| `print_bitboard(b)` | Prints a bitboard as an 8×8 grid, for debugging. |

### `position.h` / `position.cpp` — the board and the rules

| Function | What it does |
|---|---|
| `Zobrist::init()` | Fills the hash tables with fixed random numbers: one per piece per square, one for Black to move, one per castling combination, one per en passant file. |
| `Position::set_start_position()` | The normal starting array, set up through the FEN reader so only one piece of code builds a position. |
| `Position::set_from_fen(fen)` | Reads a FEN, and refuses it — leaving the position untouched — if the board is malformed, a king is missing or duplicated, a pawn stands on the first or last rank, or the side that just moved is in check. Castling rights and en passant squares survive only when the pieces they need are really there, and an en passant square only when a capture onto it is legal. |
| `Position::fen()` | Writes the position back out as a FEN. The inverse of the above. |
| `Position::compute_key()` | Builds the Zobrist key from scratch. Used when a position is set up; after that `make_move` keeps it up to date incrementally. |
| `Position::pieces(...)` | The board as bitboards: everything, one colour, one type, one colour's pieces of one or two types. |
| `Position::piece_on(s)` / `empty(s)` | The piece array: one load instead of testing twelve bitboards. |
| `Position::king_square(c)` | The square of that colour's king. |
| `Position::side_to_move()`, `ep_square()`, `castling_rights()`, `halfmove_clock()`, `fullmove_number()`, `key()`, `ply_from_root()` | The rest of the state a FEN records, plus the hash key and how deep the history is. |
| `Position::attackers_to(s, occ)` | Every piece of either colour attacking a square. The pawn case reads the attack table backwards: "a white pawn attacks s" is the same as "a black pawn on s would attack it". |
| `Position::attacked_by(s, by, ...)` | The same question, stopping at the first attacker. The `enemies` overload lets a piece about to be captured be ignored even though its square is still occupied. |
| `Position::in_check()` / `checkers()` | Whether the side to move is in check, and which pieces give it. |
| `Position::is_legal(m)` | Whether a pseudo-legal move leaves our own king safe. It assembles the resulting occupancy directly instead of playing the move, which handles pins, discovered checks and the awkward en passant case in one O(1) test. |
| `Position::make_move(m)` | Plays a move: moves the pieces, updates the hash key term by term, and pushes a state record holding everything the move destroyed. After a double push the en passant square is kept, and hashed, only if the opponent can legally take. |
| `Position::can_capture_en_passant(ep)` | Whether the side to move has a legal en passant capture onto `ep`. Only then is the square part of the position, so identical positions always get identical keys and repetitions are found. |
| `Position::unmake_move(m)` | Takes it back by popping that record. Nothing is recomputed. |
| `Position::make_null_move()` / `unmake_null_move()` | Hands the move to the opponent without moving anything, for null move pruning. Resets the count of plies since the last pass, which is what keeps repetition detection honest. |
| `Position::is_repetition(ply)` | Whether this position has occurred before. Only positions since the last capture or pawn move can match, and only every second record has the same side to move. Inside the search tree one repeat is already scored as a draw. |
| `Position::is_fifty_move_draw()` | A hundred half-moves without a capture or a pawn move. |
| `Position::is_insufficient_material()` | The material combinations in which mate is impossible: bare kings, king and one minor, two same-coloured bishops. |
| `Position::has_non_pawn_material(c)` | Whether a colour has anything but pawns and the king. Null move pruning is unsound without it. |
| `Position::see(m)` | Static exchange evaluation: plays out every recapture on the destination square, cheapest piece first, including sliders that appear behind the ones that move (x-rays), and returns the material won or lost. |
| `Position::parse_uci_move(text)` | Turns "e2e4" into the generator's move, which is how the move type (capture, double push, castling, en passant) gets filled in. |
| `Position::to_string()` | The board drawn as text with its FEN and hash key, for the `d` command. |
| `Position::put_piece` / `remove_piece` / `move_piece` | The only three ways the board changes; each keeps the bitboards, the occupancy and the piece array in step. |

### `movegen.h` / `movegen.cpp` — generating moves

| Function | What it does |
|---|---|
| `MoveList::add(m)` | Appends a move with no ordering score; the search fills that in later. |
| `MoveList::clear` / `size` / `empty` / `operator[]` / `begin` / `end` | A fixed 256-entry array, so no allocation ever happens during a search. |
| `generate_pawn_moves<Us, Type>` | Pushes, double pushes, all four promotions (with and without capture), ordinary captures and en passant — each as a handful of shifts over the whole pawn bitboard. |
| `generate_piece_moves<Us, Type>` | Knights, bishops, rooks, queens and the king: look up the attack set, mask off our own pieces, emit one move per remaining square. |
| `generate_castling<Us>` | Adds castling when the rights are there, the squares between are empty, and the king's origin, transit and destination squares are all unattacked. |
| `generate_all<Us, Type>` | Every move of one colour: pawns, then each piece type, then castling (which capture generation skips). |
| `generate_pseudo_legal(pos, list, type)` | Picks the colour and generation type once, so neither is tested inside the loops. |
| `generate_legal(pos, list)` | Generates pseudo-legal moves and keeps those that leave our king safe. Used at the root, by perft and by the move parser — not by the search, which filters lazily so that a cutoff saves the rest of the work. |

### `eval.h` / `eval.cpp` — the static evaluation

| Function | What it does |
|---|---|
| `Eval::init()` | Builds the combined material-plus-square tables, the pawn-structure masks (passed, attack span, rear span), the outpost zones and the square-distance table. |
| `Eval::clear()` | Empties the pawn hash table, so a new game starts with nothing cached. |
| `Eval::set_fifty_move_scaling(on)` | Whether the score fades as the fifty-move clock runs down. Off restores the older behaviour, where a position two moves from a draw scored like a fresh one. |
| `Eval::evaluate(pos)` | The public entry point: the evaluation with the fifty-move fade applied, i.e. `fifty_move_scale(evaluate_unscaled(pos), pos)`. |
| `Eval::evaluate_unscaled(pos)` | Builds one `Evaluation` and asks it for the score, before the fifty-move fade. This is what the transposition table stores, because its key does not include the clock. |
| `Eval::fifty_move_scale(score, pos)` | Fades a score towards zero as the fifty-move clock runs down (`score * (200 - clock) / 200`), or returns it unchanged when the option is off. |
| `Score::add` / `sub` / `operator+=` | Accumulates the middlegame and endgame halves of a term in parallel. |
| `compute_pawn_structure<Us>(pos, entry)` | Works out doubled, isolated, backward and connected pawns, and which pawns are passed, for one colour, and records them in a pawn table entry along with that colour's pawn attacks. |
| `pawn_entry(pos)` | Looks the position's pawns up in the pawn hash table, filling the entry in on a miss. Both pawn bitboards are stored and compared, so an entry is only ever used for the exact structure it came from. |
| `Evaluation::Evaluation(pos)` | Looks up (or computes) the pawn structure straight away; everything else happens in `value()`. |
| `Evaluation::initialize<Us>()` | Sets up one colour's pawn and king attack maps, its king zone, and its mobility area — the squares not covered by enemy pawns, not holding our own king, and not holding one of our stuck pawns. |
| `Evaluation::is_outpost<Us>(s)` | Whether a square is in the enemy half, defended by one of our pawns, and beyond the reach of any enemy pawn for the rest of the game. |
| `Evaluation::pieces<Us>()` | Walks every knight, bishop, rook and queen: mobility against a baseline, outposts, minor pieces behind a pawn, king protector distance, bad and long-diagonal bishops, rook files, the seventh rank and trapped rooks. On the way it fills in the attack maps and counts the attackers on the enemy king. |
| `Evaluation::king_shelter<Us>()` | The pawn cover in front of our own king, whether the files beside it are open towards enemy rooks, and how close the nearest enemy pawn on each of them has come (pawn storm). Middlegame only. |
| `Evaluation::king_safety<Us>()` | The danger our king is in: the shelter above, the squares each enemy piece type could give a safe check from, plus the attack units the enemy pieces built up, scaled by how many of them join in, with extra units for squares in the king's ring we fail to defend twice. Only counted from two attackers up. |
| `Evaluation::threats<Us>()` | Pieces attacked by our pawns, rooks and queens attacked by our minor pieces, the queen attacked by a rook, and every enemy piece we attack that nothing defends. |
| `Evaluation::passed_pawns<Us>()` | The endgame half of passed pawns: king distances to the square in front, free and unattacked paths, rooks behind the pawn, and the rule of the square. |
| `Evaluation::material_and_psqt(score)` | Material and piece-square values for every piece in one sweep, White positive and Black negative, returning the game phase counted in the same pass. |
| `Evaluation::non_pawn_material(c)` | Middlegame value of everything but pawns and the king: the material that can force a win. |
| `Evaluation::opposite_bishops()` | Whether each side has exactly one bishop and the two travel on different-coloured squares. |
| `Evaluation::scale_factor(eg)` | How much of the endgame score to believe: reduced for opposite-coloured bishops, for a winning side with no pawns and less than a rook's edge, and for knights without pawns, which cannot mate at all, and for rook-and-pawn or bishop-and-pawn against the same piece with the defending king blocking the pawn. |
| `Evaluation::known_ending(white_score)` | Recognises a lone king against KPK (exact table), rook pawns or the wrong bishop with the king in front (draws), bishop and knight (driven to the right corner) or other mating material (driven to the edge), and stalemate; returns false for everything else. |
| `push_to_edge(s)` / `push_close(d)` / `steps(a, b)` | The shapes the mating scores are built from: how near a square is to the edge, how close the kings are, and the step distance used for the bishop-and-knight corner. |
| `Evaluation::value()` | Recognised endings first (not in the tuner build), then runs every term in dependency order, scales the endgame score, blends the two phases by the game phase, and returns the result from the mover's point of view plus the tempo bonus. In the tuner build it also keeps the totals before blending for the trace. |
| `TRACE(index, colour, count)` | Records that a weight was used `count` times for a colour. Empty in the engine; in the tuner build (`EVAL_TRACE`) it fills the trace. |
| `Eval::Tune::parameters()` | Tuner build: every tunable weight with its name, current middlegame and endgame value, and which halves may be tuned. |
| `Eval::Tune::trace(pos, out)` | Tuner build: evaluates one position and returns every weight's White-minus-Black count, the totals before blending, the phase, the scale factor and the tempo. |
| `Eval::Tune::source(mg, eg)` | Tuner build: the weights as C++ definitions in `eval.cpp`'s own form, with each piece-square table's average moved into material. |

### `tune.cpp` — fitting the evaluation (tuner build only)

| Function | What it does |
|---|---|
| `Tune::run(argc, argv)` | `tuner tune positions.txt [epochs] [output] [regularisation]`: traces every position, fits K, runs Adam with a tenth held out, writes the best held-out weights and lists the largest changes. Also dispatches `explain`. |
| `Tune::explain(fen)` | `tuner explain "<FEN>"`: one position's evaluation split into every weight's contribution, largest first. |
| `load(...)` | Reads `FEN;result` lines, traces each position into a flat array of coefficients, and checks the linear model against the real evaluation. |
| `evaluate(d, i, mg, eg)` | The model's evaluation of one position for a set of weights: a dot product plus the fixed, non-linear parts. |
| `mean_error(...)` / `parallel(...)` | The mean squared error between results and expected scores, computed on every core. |

### `search.h` / `search.cpp` — finding the move

| Function | What it does |
|---|---|
| `Search::init(mb)` | Allocates the transposition table, rounded down to a power of two buckets. If the memory is not there it halves the request until it fits and says so. |
| `Search::hash_megabytes()` | The size actually in use. |
| `Search::clear()` | Forgets everything from previous searches: the table, the pawn cache, history, counter moves and killers. |
| `Search::set_threads(count)` / `Search::threads()` | How many threads search at once. The searchers are kept between searches, so their history tables survive from move to move. |
| `Search::set_move_overhead(ms)` | Time kept back from every move for the GUI to receive it. |
| `Search::set_contempt(cp)` / `Search::contempt()` | How many centipawns a draw counts against the side that is searching. Zero scores draws dead level. |
| `Search::think(pos, limits)` | The whole search: give every thread its own copy of the position, start the helpers, run thread 0 here, then stop and join the helpers and answer with thread 0's move. |
| `iterate(s, limits)` | One thread's iterative deepening: deepen a ply at a time until a limit stops it, keeping the best move from the deepest iteration that finished. On a clock it scales the soft limit by how many iterations the best move has survived (1.5× after a change down to 0.75× after four) and by 1.3× after a score drop of more than 30 cp. Helpers skip depths on their own pattern and report nothing. |
| `Search::request_stop` / `clear_stop` / `stop_requested` | The stop flag, set by the UCI thread and polled by the search. |
| `Search::set_pondering` / `ponderhit` / `pondering` | Pondering: while set, time limits are ignored; `ponderhit` restarts the clock so the move gets its full budget from that moment. |
| `Search::print_line(text)` | Writes one line to the GUI under a lock, so the two threads never interleave output. |
| `pack(data)` / `unpack(word)` | Squeeze a table entry's move, score, evaluation, depth, bound and age into one 64-bit word, and take them out again. Packing is what lets an entry be written without a lock. |
| `tt_read(entry, key, data)` | Reads both words of an entry and xors them back together. False when the entry is empty, or was caught half-written by another thread. |
| `replacement_value(data)` | How willing we are to overwrite a table entry: its depth, minus eight plies for every search it is out of date. |
| `tt_probe(key, hit, data)` | Returns the entry holding a position, or the one a new result should replace, out of a bucket of four. |
| `tt_store(...)` | Writes a result, keeping a known best move when the new result has none, and refusing to let a clearly shallower inexact result overwrite a deeper one from the same search. |
| `total_nodes()` | What every thread has searched between them, from the counts they republish every 2048 nodes. |
| `Searcher::prepare(...)` | Gets one thread ready for a new search: its own copy of the position, counters cleared, history halved rather than wiped. |
| `Searcher::clear_heuristics()` | Wipes one thread's history, continuation and capture history, counter moves and killers, for "ucinewgame". |
| `score_to_tt(score, ply)` / `score_from_tt(score, ply)` | Converts mate scores to and from being relative to the stored position, so a cached "mate in 3" stays true at another depth. |
| `tt_cutoff(entry, ...)` | Whether a stored score is good enough to return: exact, or a bound on the right side of the window. |
| `draw_value(s, side)` | What a draw is worth at a node: `Contempt` against the side that started the search, in its opponent's favour (0 when analysing with `go infinite`). Zero would make repetition the safest move in every unclear position. |
| `update_history(score, bonus)` | Moves a history score towards its limit — the closer it is, the less a bonus adds — so scores stay bounded and old information fades. A second version does the same for the 16-bit continuation and capture history entries. |
| `continuation_entry(s, ply, back, piece, to)` | The continuation history entry for playing `piece` to `to` after the move made `back` plies earlier (1: the opponent's last move, 2: our own move before it), or none after a null move or before the root. |
| `continuation_score(s, ply, piece, to)` | What the continuation history says about a quiet move: its entries after the last two moves, added up. |
| `update_quiet_histories(s, ply, us, m, piece, bonus)` | Rewards a quiet move (or with a negative bonus penalises it) in the plain history and both continuation entries. |
| `captured_type(pos, m)` | The piece type a capture takes: a pawn for en passant. |
| `counter_move(ply)` | The quiet move that last refuted the opponent's previous move. |
| `mvv_lva(pos, m)` | Orders captures by most valuable victim and least valuable attacker: PxQ before QxP. |
| `score_moves(pos, list, tt_move, ply)` | Gives every move its ordering score: table move, promotions, winning captures (by victim and attacker, ties broken by capture history), killers, the counter move, quiet moves by history plus continuation history, then losing captures. |
| `pick_move(list, i)` | Selection sort, one move at a time, so an early cutoff leaves the rest of the list unsorted and unexamined. |
| `check_time()` | Stops on the node limit, and every 2048 nodes on a stop request or the hard time limit — but never before depth 1 has produced a move, and never while pondering. |
| `qsearch(pos, alpha, beta, ply)` | Quiescence search: at depth zero, keep playing captures that do not lose material until the position is quiet, and every evasion when in check, so the engine never evaluates in the middle of an exchange. |
| `negamax(pos, depth, alpha, beta, ply, allow_null)` | The main search: table probe and cutoff, check extension, reverse futility, null move pruning, internal iterative reduction, the move loop with static exchange pruning, singular extensions, low-depth pruning and late move reductions, principal variation search, and the killer, counter move, history, continuation and capture history updates on a cutoff. Searched with an excluded move it answers the singular extension's question instead: no table cutoff or store, no forward pruning. |
| `pv_string()` | The line the engine expects both sides to play, as UCI text. |
| `score_to_uci(score)` | A score as UCI reports it: centipawns, or mate in *n* moves. |
| `hashfull()` | How full the table is, per mille, estimated from its first thousand entries. |
| `report(depth, score, bound, pv)` | One `info` line: depth, seldepth, score, nodes, time, speed, hash usage and the principal variation. |
| `set_time_limits(pos, limits)` | Turns the `go` limits into a soft limit (do not start another iteration) and a hard limit (abandon this one), from a fixed move time or from the clock. On a clock the soft limit is scaled each iteration (see `iterate`) and the hard limit allows up to 2.5 times the budget, never more than a third of the remaining time. |
| `allocate_tt(mb)` | Sizes the table to the largest power-of-two bucket count that fits. |
| `Searcher::elapsed()` / `time_used()` | Milliseconds since the search began, and since the clock for the limits started — the two differ after `ponderhit`. |

### `bitbase.h` / `bitbase.cpp` — king and pawn against king

| Function | What it does |
|---|---|
| `Bitbase::init()` | Classifies every KPK position by retrograde analysis and stores one bit per position (set: the side with the pawn wins). Called from `Eval::init()`. |
| `Bitbase::kpk_win(strong, king, pawn, weak_king, stm)` | Whether the side with the pawn wins: mirrors the position so that side is White and the pawn on files a to d, and reads the bit. |
| `index(stm, black_king, white_king, pawn)` | Packs a position into 18 bits: both kings, the side to move, the pawn's file (a-d) and rank. |
| `Entry::Entry(idx)` | The result that can be read off a position directly: impossible, a safe promotion (win), stalemate or the pawn taken (draw), or unknown. |
| `Entry::classify(db)` | One step of the analysis: White wins if any move wins; Black draws if any move draws; otherwise the other result once every move is known. |

### `perft.h` / `perft.cpp` — proving the move generator

| Function | What it does |
|---|---|
| `perft(pos, depth)` | Counts the leaf nodes of the legal move tree by playing every legal move and recursing. No evaluation and no pruning: the point is to count exactly what the rules allow. |
| `perft_divide(pos, depth)` | The same count broken down by first move. This is how a bug is found: compare per-move totals against a known-good engine, play the move that differs, repeat. |
| `run_perft_suite()` | Runs seven standard positions against their published counts — about 17 million nodes covering castling, en passant, promotions, pins and check evasion. |

### `uci.cpp` — talking to the GUI

| Function | What it does |
|---|---|
| `wait_for_search()` / `stop_search()` | Wait for the search thread, and the same with a stop request first. Every path that ends a search goes through one of these, so no thread is ever left running. |
| `to_lower(text)` / `trim(text)` | Case folding for option names (UCI matches them case-insensitively) and whitespace trimming for values. |
| `parse_int(token, value)` | Parses a whole token as an integer, rejecting anything with leftovers, so a malformed number cannot be read as a valid one. |
| `is_command(token)` / `is_go_keyword(token)` | The words the engine answers to, and the words that introduce a `go` parameter. The first lets unknown leading tokens be skipped, the second lets `searchmoves` know where its list ends. |
| `cmd_position(pos, in)` | Builds the new position on the side and only adopts it if the FEN is valid, so a bad command cannot leave the engine with a broken board. |
| `cmd_go(pos, in)` | Parses every `go` parameter, applies the default move time when no limit was given, and starts the search on its own thread with its own copy of the position. The thread holds its answer back until `stop` or `ponderhit` when it must. |
| `cmd_setoption(in)` | Handles `Hash`, `Clear Hash`, `Threads`, `Move Overhead`, `Ponder`, `Contempt` and `Fifty Move Scaling`, reporting bad values and unknown options as `info string`. |
| `cmd_bench(depth)` | Searches a fixed set of positions to a fixed depth and reports nodes and speed. The node count is exactly reproducible, so it is the number to compare between versions. |
| `bench_depth(text)` | The depth argument for `bench`, defaulting to 9. |
| `main(argc, argv)` | Start-up, the shell shortcuts (`test`, `bench`, `perft`), then the UCI loop: read a line, answer it, repeat. The loop keeps reading while a search runs, which is what makes `stop`, `isready` and `ponderhit` work mid-search. |

## Known limitations

- No MultiPV: only one line is reported, however many threads are searching.
- Contempt stops the engine repeating, but a genuinely equal position it cannot
  make progress in still ends drawn -- by the fifty-move rule instead. Contempt
  is also a fixed number: it does not rise when the engine is winning or fall
  when it is losing, as stronger engines' does.
- With more than one thread the search is not reproducible, and the threads
  share one transposition table, so scaling falls off as they compete for it.
- No opening book and no endgame tablebases.
- The evaluation's weights are fitted to self-play results (Texel tuning), but the
  king-danger formula, the endgame scale factors and every pruning and reduction
  parameter in the search are still hand-picked.
- No specialised mate knowledge: king, bishop and knight against a bare king is
  a win the engine has to find by searching.
- The transposition table's search scores still depend on things its key does
  not record: the fifty-move clock (through the fade) and, with contempt, which
  side started the search. Static evaluations no longer do. The effect is small
  and entries age out, but analysing the same position for both sides in a row
  can show slightly different scores; `Clear Hash` removes it.
- Standard chess only, not Chess960.

## Where to take it next

Roughly in order of Elo gained per hour of work:

1. **More endgame knowledge** — more exact tables (KRK, KQKR, KPKP) or Syzygy
   tablebases, and more drawing patterns.
2. **More search** — SPSA tuning of the search parameters (margins, depths, time
   scales), correction history, history-based pruning of quiet moves, using the
   table score as a better static evaluation, razoring, and another try at
   "improving" now that the evaluation is tuned.
3. **The king-danger formula** — still hand-picked; reshaped so it can be tuned
   with the rest.
4. **NNUE** — a neural-network evaluation trained on the engine's own games; the
   tuning data pipeline (`--datagen`) is the first part of it.
5. **Better threading** — the helpers currently differ only in their history and
   the depths they skip; giving them different aspiration windows or root move
   orders would spread them further apart.
6. **Fewer drawn games** — dynamic contempt (more when ahead, less when behind),
   and something that makes progress attractive in equal positions, so refusing
   a repetition does not simply end in a fifty-move draw instead.
