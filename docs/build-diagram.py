#!/usr/bin/env python3
"""Builds engine-13.html: a page of hand-laid SVG diagrams of how Bitboard Engine 13 works.

    python3 docs/build-diagram.py

Every figure is laid out by hand here, with the numbers from Engine_13's source;
change them here (and rerun) when the engine changes.
"""

from html import escape
from pathlib import Path

OUT = Path(__file__).with_name("engine-13.html")


# ----------------------------------------------------------------------------
# SVG helpers
# ----------------------------------------------------------------------------

def e(s):
    return escape(s, quote=True)


def defs(p):
    """Arrow markers for one figure; ids are prefixed so figures never clash."""
    out = ['<defs>']
    for name, cls in (("ink", "mk"), ("pr", "mkp"), ("ac", "mka")):
        out.append(
            f'<marker id="{p}-{name}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" '
            f'markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="{cls}"/></marker>')
    out.append(f'<pattern id="{p}-hatch" width="6" height="6" patternUnits="userSpaceOnUse" '
               f'patternTransform="rotate(45)"><rect width="6" height="6" class="hatchbg"/>'
               f'<line x1="0" y1="0" x2="0" y2="6" class="hatch"/></pattern>')
    out.append('</defs>')
    return "".join(out)


def text_lines(x, cy, lines, anchor="middle"):
    """lines: list of (text, cls). Vertically centred on cy, 16 px apart."""
    n = len(lines)
    y0 = cy - 16 * (n - 1) / 2 + 4.5
    return "".join(
        f'<text class="{c}" x="{x:.1f}" y="{y0 + i * 16:.1f}" text-anchor="{anchor}">{e(t)}</text>'
        for i, (t, c) in enumerate(lines))


def box(x, y, w, h, lines, cls="bx", align="middle"):
    tx = x + w / 2 if align == "middle" else x + 14
    return (f'<rect class="{cls}" x="{x}" y="{y}" width="{w}" height="{h}" rx="6"/>'
            + text_lines(tx, y + h / 2, lines, "middle" if align == "middle" else "start"))


def T(s):
    return (s, "t")


def S(s):
    return (s, "s")


def M(s):
    return (s, "m")


def P(s):
    return (s, "pt")


def path(p, pts, kind="ink", cls="ln", start=False, end=True):
    d = " ".join(f"{'M' if i == 0 else 'L'}{x:.1f},{y:.1f}" for i, (x, y) in enumerate(pts))
    extra = {"ink": "", "pr": " lnp", "ac": " lna"}[kind]
    ms = f' marker-start="url(#{p}-{kind})"' if start else ""
    me = f' marker-end="url(#{p}-{kind})"' if end else ""
    return f'<path class="{cls}{extra}" d="{d}"{ms}{me}/>'


def label(x, y, s, cls="lb", anchor="middle", rotate=None):
    tr = f' transform="rotate({rotate} {x} {y})"' if rotate is not None else ""
    return f'<text class="{cls}" x="{x:.1f}" y="{y:.1f}" text-anchor="{anchor}"{tr}>{e(s)}</text>'


def svg(p, w, h, body, aria, minw=680):
    return (f'<svg viewBox="0 0 {w} {h}" role="img" aria-label="{e(aria)}" '
            f'style="min-width:{minw}px">{defs(p)}{body}</svg>')


# ----------------------------------------------------------------------------
# Figure 1: the whole engine
# ----------------------------------------------------------------------------

def fig_overview():
    p = "ov"
    b = []
    b.append(box(20, 60, 150, 84, [T("GUI"), S("Chess.app, match runner"), S("or any UCI program")]))
    b.append(box(290, 60, 200, 84, [T("UCI thread"), M("uci.cpp"), S("keeps reading commands")]))
    b.append(box(600, 60, 220, 84, [T("Search thread"), M("Search::think()"), S("8 MB stack (thread.h)")]))
    b.append(path(p, [(170, 90), (290, 90)]))
    b.append(label(230, 82, "position, go, stop"))
    b.append(path(p, [(290, 118), (170, 118)]))
    b.append(label(230, 134, "info, bestmove"))
    b.append(path(p, [(490, 90), (600, 90)]))
    b.append(label(545, 82, "go: start"))
    b.append(path(p, [(600, 118), (490, 118)]))
    b.append(label(545, 134, "best + ponder"))

    # think() starts the helpers and runs thread 0 itself.
    b.append(path(p, [(710, 144), (710, 176)], end=False))
    b.append(path(p, [(550, 176), (890, 176)], end=False))
    for x in (550, 720, 890):
        b.append(path(p, [(x, 176), (x, 210)]))
    b.append(label(718, 167, "starts helpers, joins them", anchor="start"))
    b.append(box(470, 210, 160, 76, [T("Thread 0 (main)"), M("iterate()"), S("reports, plays its move")], "bx"))
    b.append(box(640, 210, 160, 76, [T("Helper 1"), M("iterate()"), S("skips some depths")], "bx"))
    b.append(box(810, 210, 160, 76, [T("Helpers 2 … n"), M("iterate()"), S("own tables each")], "bx"))

    b.append(box(470, 330, 500, 60, [T("Transposition table · shared"),
                                     S("buckets of 4 entries, 64 bytes · key ⊕ data, no locks")], "bxa"))
    for x in (550, 720, 890):
        b.append(path(p, [(x, 286), (x, 330)], kind="ac", start=True))
    b.append(label(556, 312, "probe / store", anchor="start"))

    b.append(box(20, 210, 380, 180, [T("Start-up, once"), S("attack tables + magic multipliers"), S("Zobrist hash keys"),
                                     S("evaluation tables"), S("king + pawn vs king bitbase"),
                                     S("transposition table, 128 MB")], align="start"))
    b.append(path(p, [(390, 210), (390, 144)]))
    b.append(label(382, 184, "then the UCI loop", anchor="end"))
    return svg(p, 1000, 410, "".join(b),
               "The GUI talks UCI to the UCI thread, which starts a search thread; it runs thread 0 and "
               "helper threads that share one transposition table.")


# ----------------------------------------------------------------------------
# Figure 2: the move word
# ----------------------------------------------------------------------------

def bitfield(p, x0, y, width, fields, bits=16, h=44, cap=""):
    """fields: (name, nbits, cls) from the most significant bit down."""
    px = width / bits
    out = []
    x = x0
    hi = bits - 1
    for name, n, cls in fields:
        w = n * px
        out.append(f'<rect class="{cls}" x="{x:.1f}" y="{y}" width="{w:.1f}" height="{h}"/>')
        out.append(label(x + w / 2, y + h / 2 + 4.5, name, "t"))
        out.append(label(x + 3, y - 6, str(hi), "lb", "start"))
        if n > 1 and w >= 50:
            out.append(label(x + w - 3, y - 6, str(hi - n + 1), "lb", "end"))
        hi -= n
        x += w
    return "".join(out)


def fig_move():
    p = "mv"
    b = [bitfield(p, 60, 40, 880, [("type", 4, "bxa"), ("to square", 6, "bx"), ("from square", 6, "bx")])]
    b.append(label(60 + 880 / 16 * 1.5, 112, "bit 14: capture", "lb"))
    b.append(label(60 + 880 / 16 * 0.5, 128, "bit 15: promotion", "lb", "start"))
    b.append(path(p, [(60 + 880 / 16 * 1.5, 100), (60 + 880 / 16 * 1.5, 86)], end=True))
    b.append(label(500, 128, "squares a1 = 0 … h8 = 63, rank by rank", "lb"))
    return svg(p, 1000, 140, "".join(b),
               "A move is 16 bits: 4 bits of type, 6 bits of destination, 6 bits of origin.", minw=560)


# ----------------------------------------------------------------------------
# Figure 3: iterative deepening
# ----------------------------------------------------------------------------

def fig_deepening():
    p = "id"
    b = []
    b.append(box(20, 50, 150, 64, [T("depth d"), S("1, 2, 3, …")]))
    b.append(box(200, 50, 190, 64, [T("aspiration window"), S("last score ± 21 cp (d ≥ 4)")]))
    b.append(box(420, 50, 170, 64, [T("search the root"), M("negamax(d)")]))
    b.append(box(620, 50, 170, 64, [T("score inside"), T("the window?")]))
    b.append(box(820, 50, 160, 64, [T("keep the best move"), S("print the info line")]))
    for x1, x2 in ((170, 200), (390, 420), (590, 620), (790, 820)):
        b.append(path(p, [(x1, 82), (x2, 82)]))
    b.append(label(805, 74, "yes"))
    b.append(path(p, [(705, 50), (705, 22), (505, 22), (505, 50)]))
    b.append(label(605, 15, "no: widen the window, search again"))
    b.append(path(p, [(900, 114), (900, 210)]))
    b.append(box(820, 210, 160, 64, [T("time for"), T("depth d + 1?")]))
    b.append(box(590, 210, 200, 64, [T("stop"), S("send bestmove")], "bxa"))
    b.append(path(p, [(820, 242), (790, 242)]))
    b.append(label(805, 236, "no"))
    b.append(path(p, [(900, 274), (900, 306), (95, 306), (95, 114)]))
    b.append(label(500, 300, "yes: d + 1, reusing everything the table and history tables learned"))
    return svg(p, 1000, 320, "".join(b),
               "Iterative deepening: each depth is searched inside an aspiration window, widened on failure, "
               "until time runs out.")


# ----------------------------------------------------------------------------
# Figure 4: one node of negamax
# ----------------------------------------------------------------------------

def fig_node():
    p = "nd"
    SX, SW = 140, 420
    CX = SX + SW / 2
    PX, PW = 640, 340
    b = []
    y = 20
    prev_bottom = None
    groups = []
    pos = {}

    def h_for(n):
        return 24 + 16 * n

    seq = [
        ("box", "start", [T("A node: position, depth, α, β, ply")], None),
        ("box", "draw", [T("Draw or mate already decided?"),
                         S("repetition · fifty moves · no mating material · mate distance")],
         [P("return the draw score (± contempt)")]),
        ("box", "q", [T("depth ≤ 0 ?")], [P("go to quiescence search (§6)")]),
        ("box", "tt", [T("Probe the transposition table"), S("its move is tried first either way")],
         [P("return the stored score: deep enough, bound fits")]),
        ("box", "eval", [T("Static evaluation (§7)"), S("+ correction history for this pawn structure"),
                         S("a table bound on the right side replaces it")], None),
        ("box", "iir", [T("No table move and depth ≥ 4: one ply shallower")], None),
        ("group", "Before any move (non-PV, no check)"),
        ("box", "rfp", [T("Reverse futility · depth ≤ 6"), S("eval − 89 × depth ≥ β")], [P("return eval")]),
        ("box", "razor", [T("Razoring · depth ≤ 3"), S("eval + 246 × depth < α, quiescence agrees")],
         [P("return the quiescence score")]),
        ("box", "null", [T("Null move · depth ≥ 3, eval ≥ β, has pieces"),
                         S("pass; search at depth − (2 + depth/4 + (eval − β)/201)")],
         [P("return β: still ≥ β after passing a move")]),
        ("end",),
        ("box", "sing", [T("Singular extension · depth ≥ 8, table lower bound"),
                         S("other moves at depth/2 must stay below table score − 2 × depth"),
                         S("if they do, the table move gets +1 ply")],
         [P("multi-cut: several moves beat β, return β")]),
        ("group", "Move loop · best moves first (§5)"),
        ("box", "pick", [T("Pick the next move"), S("none left: leave the loop")], None),
        ("box", "legal", [T("Legal?"), S("tested without making the move")], None),
        ("box", "skip", [T("Skip it?"),
                         S("SEE: capture loses > 102 × d, quiet > 39 × d² (d ≤ 8)"),
                         S("futility: eval + 19 + 100 × d ≤ α (d ≤ 3)"),
                         S("late-move: beyond 6 / 10 / 16 quiets (d ≤ 3)")], None),
        ("box", "ext", [T("Extend or reduce"), S("in check after the move: +1 ply · singular: +1 ply"),
                        S("LMR, late quiet, d ≥ 3: 0.79 + ln d × ln m / 2.15, ± history")], None),
        ("box", "pvs", [T("Search the child (PVS)"), S("null window first, full window if it beats α"),
                        S("a reduced search that beats α is redone at full depth")], None),
        ("box", "beta", [T("Score ≥ β ?")],
         [P("β-cutoff: reward the move as killer, counter"), P("and in the histories; leave the loop")]),
        ("end",),
        ("box", "mate", [T("No legal move: checkmate or stalemate")], [P("return −mate + ply, or 0")]),
        ("box", "corr", [T("Update correction history"), S("trustworthy score, quiet best move")], None),
        ("box", "store", [T("Store in the table"), S("move, score, bound, depth, eval")], None),
    ]

    for item in seq:
        if item[0] == "group":
            y += 0
            groups.append([item[1], y, None])
            y += 40
            continue
        if item[0] == "end":
            groups[-1][2] = y - 26 + 16
            y = groups[-1][2] + 26
            continue
        _, key, lines, exit_ = item
        h = h_for(len(lines))
        if prev_bottom is not None:
            b.append(path(p, [(CX, prev_bottom), (CX, y)]))
        cls = "bxa" if key == "start" else "bx"
        b.append(box(SX, y, SW, h, lines, cls))
        cy = y + h / 2
        pos[key] = (y, h, cy)
        if exit_:
            ph = 20 + 16 * len(exit_)
            b.append(path(p, [(SX + SW, cy), (PX, cy)], kind="pr"))
            b.append(f'<rect class="pill" x="{PX}" y="{cy - ph / 2:.1f}" width="{PW}" height="{ph}" rx="{ph / 2:.1f}"/>')
            b.append(text_lines(PX + PW / 2, cy, exit_))
            if key in ("draw", "q", "beta"):
                b.append(label(SX + SW + 40, cy - 6, "yes", "lbp"))
        prev_bottom = y + h
        y += h + 26

    # End of the node.
    b.append(path(p, [(CX, prev_bottom), (CX, y)]))
    b.append(f'<rect class="pill pilla" x="{CX - 120}" y="{y}" width="240" height="36" rx="18"/>')
    b.append(label(CX, y + 22.5, "return the best score", "t"))
    total_h = y + 56

    # Group frames, drawn under nothing (frames only).
    frames = []
    for title, gy, gend in groups:
        frames.append(f'<rect class="grp" x="100" y="{gy}" width="510" height="{gend - gy}" rx="10"/>')
        frames.append(label(CX + 12, gy + 24, title.upper(), "gt", "start"))

    # The loop: from "Score ≥ β ?" (no) back to "Pick the next move".
    by, bh, bcy = pos["beta"]
    py_, ph_, pcy = pos["pick"]
    b.append(path(p, [(SX, bcy), (120, bcy), (120, pcy), (SX, pcy)]))
    b.append(label(132, bcy - 6, "no", "lb", "start"))
    for key in ("legal", "skip"):
        _, _, cy = pos[key]
        b.append(path(p, [(SX, cy), (120, cy)], end=False))
    b.append(label(113, (pcy + bcy) / 2, "next move", "lb", "middle", rotate=-90))

    # A cutoff leaves the loop and goes on to the correction-history update.
    _, _, ccy = pos["corr"]
    b.append(path(p, [(PX + PW, bcy), (992, bcy), (992, ccy), (SX + SW, ccy)], kind="pr"))

    return svg(p, 1000, total_h, "".join(frames) + "".join(b),
               "What happens at one node of the negamax search, from the draw check through pruning, "
               "the move loop and the table store; amber boxes are the early returns.", minw=720), total_h


# ----------------------------------------------------------------------------
# Figure 5: move ordering
# ----------------------------------------------------------------------------

def fig_ordering():
    p = "mo"
    b = []
    rungs = [
        ("Transposition-table move", "the best move stored for this position"),
        ("Promotions", ""),
        ("Winning and even captures", "SEE ≥ 0 · by victim, then attacker · capture history on ties"),
        ("Killer moves, 2 per ply", "quiet moves that caused a cutoff at this ply"),
        ("Counter move", "the quiet move that last refuted the opponent's move"),
        ("Other quiet moves", "history + continuation history (1 and 2 moves back)"),
        ("Losing captures", "SEE < 0, tried last"),
    ]
    for i, (t, s) in enumerate(rungs):
        y = 20 + i * 56
        lines = [T(t)] + ([S(s)] if s else [])
        b.append(f'<rect class="{"bxa" if i == 0 else "bx"}" x="20" y="{y}" width="560" height="46" rx="6"/>')
        b.append(f'<circle class="num" cx="44" cy="{y + 23}" r="12"/>')
        b.append(label(44, y + 27.5, str(i + 1), "numt"))
        b.append(text_lines(70, y + 23, lines, "start"))
    tables = [
        (0, "Transposition table", "bxa"),
        (2, "Capture history", "bx"),
        (3, "Killers", "bx"),
        (4, "Counter moves", "bx"),
        (5, "History + continuation history", "bx"),
    ]
    b.append(label(840, 104, "LEARNED DURING THE SEARCH", "gt"))
    for rung, name, cls in tables:
        y = 20 + rung * 56
        b.append(box(700, y + 4, 280, 38, [T(name)], cls))
        b.append(path(p, [(700, y + 23), (580, y + 23)], kind="ac" if cls == "bxa" else "ink"))
    return svg(p, 1000, 420, "".join(b),
               "Moves are tried in seven bands, from the table move to losing captures; the tables on the "
               "right, updated at every cutoff, decide the order inside the bands.")


# ----------------------------------------------------------------------------
# Figure 6: quiescence search
# ----------------------------------------------------------------------------

def fig_qsearch():
    p = "qs"
    b = []
    b.append(box(20, 40, 160, 60, [T("In check?")]))
    b.append(path(p, [(100, 100), (100, 150)]))
    b.append(label(108, 130, "yes", "lb", "start"))
    b.append(box(20, 150, 160, 70, [T("every evasion"), S("none: checkmate")]))
    b.append(path(p, [(180, 70), (220, 70)]))
    b.append(label(200, 62, "no"))
    b.append(box(220, 40, 200, 60, [T("stand pat"), S("corrected static eval")]))
    b.append(path(p, [(420, 70), (460, 70)]))
    b.append(box(460, 40, 170, 60, [T("eval ≥ β ?")]))
    b.append(path(p, [(545, 100), (545, 160)], kind="pr"))
    b.append(label(553, 134, "yes", "lbp", "start"))
    b.append(f'<rect class="pill" x="465" y="160" width="160" height="36" rx="18"/>')
    b.append(text_lines(545, 178, [P("return eval")]))
    b.append(path(p, [(630, 70), (670, 70)]))
    b.append(label(650, 62, "no"))
    b.append(box(670, 40, 310, 60, [T("captures + promotions, SEE ≥ 0"), S("skip if eval + victim + 208 < α")]))
    b.append(path(p, [(825, 100), (825, 150)]))
    b.append(box(670, 150, 310, 70, [T("search each the same way"), S("store the result in the table at depth 0")]))
    return svg(p, 1000, 240, "".join(b),
               "Quiescence search: stand pat on the evaluation, or play captures until the position is quiet.")


# ----------------------------------------------------------------------------
# Figure 7: the evaluation
# ----------------------------------------------------------------------------

def fig_eval():
    p = "ev"
    b = []
    # Known endings come first and short-circuit everything.
    b.append(box(20, 10, 800, 44, [T("Known ending?  king + pawn vs king bitbase · mating material vs a lone king · known draws · stalemate")],
                 align="start"))
    b.append(path(p, [(820, 32), (860, 32)], kind="pr"))
    b.append(f'<rect class="pill" x="860" y="14" width="200" height="36" rx="18"/>')
    b.append(text_lines(960, 32, [P("return that score")]))
    b.append(path(p, [(91.5, 54), (91.5, 96)]))
    b.append(label(99, 84, "otherwise", "lb", "start"))

    terms = [
        ("Material", ["+ piece-square tables"], "both"),
        ("Pawn structure", ["doubled, isolated,", "backward, connected", "cached in a pawn hash"], "both"),
        ("Pieces", ["mobility, outposts,", "rook files, bishops"], "both"),
        ("King safety", ["shield, storms,", "safe checks, attackers"], "mg"),
        ("Threats", ["attacked by pawns,", "hanging pieces"], "both"),
        ("Passed pawns", ["king distance, path,", "rule of the square"], "eg"),
    ]
    W, G = 143, 12
    EG_Y, MG_Y = 236, 276
    centers = []
    for i, (t, subs, feeds) in enumerate(terms):
        x = 20 + i * (W + G)
        c = x + W / 2
        centers.append(c)
        b.append(box(x, 96, W, 84, [T(t)] + [S(s) for s in subs]))
        if feeds in ("both", "eg"):
            b.append(path(p, [(c - 18, 180), (c - 18, EG_Y)], end=False))
            b.append(f'<circle class="dot" cx="{c - 18:.1f}" cy="{EG_Y}" r="4"/>')
        if feeds in ("both", "mg"):
            b.append(path(p, [(c + 18, 180), (c + 18, MG_Y)], end=False))
            b.append(f'<circle class="dot" cx="{c + 18:.1f}" cy="{MG_Y}" r="4"/>')

    # The piece walk builds the attack maps the later terms read.
    b.append(path(p, [(centers[2], 96), (centers[2], 76), (centers[5], 76)], kind="ac", end=False))
    for c in centers[3:]:
        b.append(path(p, [(c, 76), (c, 96)], kind="ac"))
    b.append(label((centers[3] + centers[4]) / 2, 70, "attack maps from the piece walk", "lba"))

    # Two running totals.
    b.append(f'<line class="bus" x1="20" y1="{EG_Y}" x2="955" y2="{EG_Y}"/>')
    b.append(f'<line class="bus" x1="20" y1="{MG_Y}" x2="945" y2="{MG_Y}"/>')
    b.append(label((centers[0] + centers[1]) / 2, EG_Y - 7, "endgame total", "lb"))
    b.append(label((centers[0] + centers[1]) / 2, MG_Y - 7, "middlegame total", "lb"))
    b.append(box(955, 214, 105, 44, [T("× scale"), S("drawish material")]))
    b.append(path(p, [(1007.5, 258), (1007.5, 340)]))
    b.append(path(p, [(945, MG_Y), (945, 340)]))

    # Blend, then the finishing steps, right to left.
    b.append(box(840, 340, 220, 60, [T("Blend by game phase p"), S("(mg × p + eg × (24 − p)) / 24")]))
    b.append(path(p, [(840, 370), (800, 370)]))
    b.append(box(600, 340, 200, 60, [T("Side to move's view"), S("+ 12 tempo")]))
    b.append(path(p, [(600, 370), (560, 370)]))
    b.append(box(330, 340, 230, 60, [T("Fifty-move fade"), S("× (200 − clock) / 200")]))
    b.append(path(p, [(330, 370), (290, 370)]))
    b.append(box(20, 340, 270, 60, [T("Score in centipawns"), S("for the side to move")], "bxa"))
    return svg(p, 1080, 420, "".join(b),
               "The evaluation: known endings first, otherwise six groups of terms add to a middlegame and an "
               "endgame total, which are scaled, blended by game phase, given tempo and faded near the fifty-move rule.",
               minw=760)


# ----------------------------------------------------------------------------
# Figure 8: time management, drawn to scale
# ----------------------------------------------------------------------------

def fig_time():
    p = "tm"
    b = []
    X0, X1 = 80, 940

    def xb(v):  # budget units, 0 .. 2.5 B
        return X0 + (X1 - X0) * v / 2.5

    b.append(label(20, 40, "ON A CLOCK", "gt", "start"))
    y, h = 56, 30
    b.append(f'<rect class="seg-run" x="{X0}" y="{y}" width="{xb(0.45) - X0:.1f}" height="{h}"/>')
    b.append(f'<rect class="seg-soft" x="{xb(0.45):.1f}" y="{y}" width="{xb(0.9) - xb(0.45):.1f}" height="{h}" fill="url(#{p}-hatch)"/>')
    b.append(f'<rect class="seg-drop" x="{xb(0.9):.1f}" y="{y}" width="{xb(1.17) - xb(0.9):.1f}" height="{h}"/>')
    b.append(f'<rect class="seg-fin" x="{xb(1.17):.1f}" y="{y}" width="{X1 - xb(1.17):.1f}" height="{h}"/>')
    b.append(f'<line class="hard" x1="{X1}" y1="{y - 8}" x2="{X1}" y2="{y + h + 8}"/>')
    for v, name in ((0, "0"), (0.6, "0.6 B"), (1.0, "B"), (2.5, "2.5 B")):
        x = xb(v)
        b.append(f'<line class="tick" x1="{x:.1f}" y1="{y + h}" x2="{x:.1f}" y2="{y + h + 6}"/>')
        b.append(label(x, y + h + 20, name, "m"))
    b.append(label((xb(0.45) + xb(0.9)) / 2, y - 8, "no new depth after 0.6 B × stability", "lb"))
    b.append(label((xb(0.9) + xb(1.17)) / 2 + 30, y - 22, "× 1.3 when the score drops > 30 cp", "lb"))
    b.append(path(p, [((xb(0.9) + xb(1.17)) / 2, y - 18), ((xb(0.9) + xb(1.17)) / 2, y - 2)], end=True))
    b.append(label((xb(1.17) + X1) / 2, y + h / 2 + 4.5, "only the iteration already running", "s"))
    b.append(label(X1, y - 12, "hard stop", "lbp", "end"))

    chips = [("best move just changed", "1.5×"), ("stable 1 iteration", "1.2×"), ("2", "1.0×"),
             ("3", "0.85×"), ("4 or more", "0.75×")]
    cx = X0
    b.append(label(20, 150, "stability", "lb", "start"))
    for name, v in chips:
        w = 54 + 6.3 * len(name)
        b.append(f'<rect class="chip" x="{cx:.1f}" y="136" width="{w:.1f}" height="24" rx="12"/>')
        b.append(label(cx + 10, 152, v, "m", "start"))
        b.append(label(cx + 44, 152, name, "lb", "start"))
        cx += w + 10
    b.append(label(20, 186, "B = time left ÷ moves to go (30 if not given) + ¾ × increment, at most a third of the clock",
                   "s", "start"))

    b.append(label(20, 228, "FIXED TIME PER MOVE (go movetime T; a plain go means 500 ms)", "gt", "start"))
    y2 = 244

    def xt(t):
        return X0 + (X1 - X0) * t

    b.append(f'<rect class="seg-run" x="{X0}" y="{y2}" width="{xt(0.9) - X0:.1f}" height="{h}"/>')
    b.append(f'<rect class="seg-soft" x="{xt(0.9):.1f}" y="{y2}" width="{X1 - xt(0.9):.1f}" height="{h}" fill="url(#{p}-hatch)"/>')
    b.append(f'<line class="hard" x1="{X1}" y1="{y2 - 8}" x2="{X1}" y2="{y2 + h + 8}"/>')
    b.append(label((X0 + xt(0.9)) / 2, y2 + h / 2 + 4.5, "starts new depths", "s"))
    for t, name in ((0, "0"), (0.9, "0.9 T"), (1.0, "T")):
        x = xt(t)
        b.append(f'<line class="tick" x1="{x:.1f}" y1="{y2 + h}" x2="{x:.1f}" y2="{y2 + h + 6}"/>')
        b.append(label(x, y2 + h + 20, name, "m"))
    return svg(p, 1000, 310, "".join(b),
               "Time management drawn to scale: on a clock no new depth starts after 0.6 of the budget times a "
               "stability factor, and the search stops at 2.5 budgets; with a fixed time, at 90% and 100%.")


# ----------------------------------------------------------------------------
# Figure 9: Lazy SMP depths and the table entry
# ----------------------------------------------------------------------------

SKIP_SIZE = [1, 1, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4]
SKIP_PHASE = [0, 1, 0, 1, 2, 0, 1, 2, 3, 4, 5, 6, 7, 8, 0, 1, 2, 3, 4, 5]


def searches(thread, depth):
    if thread == 0:
        return True
    i = (thread - 1) % 20
    return ((depth + SKIP_PHASE[i]) // SKIP_SIZE[i]) % 2 == 0


def fig_smp():
    p = "sm"
    b = []
    X0, CW, CH = 170, 62, 30
    b.append(label(20, 24, "DEPTH", "gt", "start"))
    for d in range(1, 13):
        b.append(label(X0 + (d - 1) * CW + CW / 2, 24, str(d), "m"))
    names = ["Thread 0 (main)", "Helper 1", "Helper 2", "Helper 3"]
    for t, name in enumerate(names):
        y = 36 + t * 40
        b.append(label(20, y + 20, name, "t", "start"))
        for d in range(1, 13):
            x = X0 + (d - 1) * CW
            cls = "cell-on" if searches(t, d) else "cell-off"
            b.append(f'<rect class="{cls}" x="{x + 3}" y="{y}" width="{CW - 6}" height="{CH}" rx="4"/>')
    return svg(p, 930, 200, "".join(b),
               "Which depths each thread searches: thread 0 searches every depth; helper 1 the even ones, "
               "helper 2 the odd ones, helper 3 two on, two off.", minw=640)


def fig_tt():
    p = "tt"
    b = []
    b.append(label(20, 22, "DATA WORD (64 BITS)", "gt", "start"))
    b.append(bitfield(p, 20, 46, 960, [("age", 6, "bx"), ("bnd", 2, "bx"), ("depth + 1", 8, "bx"),
                                       ("static eval", 16, "bx"), ("score", 16, "bxa"), ("best move", 16, "bxa")],
                      bits=64))
    b.append(label(20, 126, "KEY WORD = position key ⊕ data word", "gt", "start"))
    b.append(label(20, 146, "A reader recomputes key word ⊕ data word; if another thread wrote one of the two in between, "
                   "the key will not match and it counts as a miss.", "s", "start"))
    b.append(label(20, 186, "BUCKET = 4 ENTRIES × 16 BYTES = ONE 64-BYTE CACHE LINE", "gt", "start"))
    for i in range(4):
        x = 20 + i * 242
        b.append(f'<rect class="bx" x="{x}" y="198" width="114" height="34" rx="4"/>')
        b.append(f'<rect class="bxa" x="{x + 118}" y="198" width="114" height="34" rx="4"/>')
        b.append(label(x + 57, 219.5, "key word", "s"))
        b.append(label(x + 175, 219.5, "data word", "s"))
    b.append(label(20, 260, "Replace first: the entry with the lowest depth − 8 × (searches since it was written).",
                   "s", "start"))
    return svg(p, 1000, 272, "".join(b),
               "A table entry is two 64-bit words: a packed data word and the key xored with it; four entries make "
               "a 64-byte bucket.", minw=720)


# ----------------------------------------------------------------------------
# Page
# ----------------------------------------------------------------------------

PARAMS = [
    ("Reverse futility", "89 cp × depth", "depth ≤ 6"),
    ("Razoring", "246 cp × depth", "depth ≤ 3"),
    ("Null-move reduction", "2 + depth / 4 + (eval − β) / 201", "extra part at most 3"),
    ("Futility", "19 + 100 cp × depth", "depth 1–3, quiet moves"),
    ("SEE pruning", "102 cp × depth (captures), 39 cp × depth² (quiets)", "depth ≤ 8"),
    ("Late move reductions", "0.79 + ln depth × ln move / 2.15", "history moves it by up to ±3"),
    ("LMR history divisor", "16418", "history ÷ this = plies"),
    ("Singular extension", "from depth 8, margin 2 cp × depth", ""),
    ("Aspiration window", "± 21 cp", "from depth 4"),
    ("Delta pruning", "208 cp", "quiescence search"),
]


def page():
    node_svg, _ = fig_node()
    rows = "".join(f"<tr><td>{e(a)}</td><td class='num'>{e(b)}</td><td class='note'>{e(c)}</td></tr>"
                   for a, b, c in PARAMS)
    return f"""<title>Inside Bitboard Engine 13</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:opsz,wght@12..96,600;12..96,800&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
/* Layout: one reading column of prose, figures allowed wider; each figure is a labelled plate. */
:root {{
  --bg: #eef1ef;
  --surface: #ffffff;
  --ink: #17201d;
  --muted: #56635e;
  --line: #9aa8a2;
  --accent: #1b5d84;
  --acc-soft: #dbe9f2;
  --prune: #a65c12;
  --prune-soft: #f6e6d3;
  --shade: #e2e8e5;
  --display: "Bricolage Grotesque", "Avenir Next", "Segoe UI", sans-serif;
  --sans: "IBM Plex Sans", "Helvetica Neue", Arial, sans-serif;
  --mono: "IBM Plex Mono", ui-monospace, Menlo, Consolas, monospace;
}}
@media (prefers-color-scheme: dark) {{
  :root:not([data-theme="light"]) {{
    --bg: #111715; --surface: #18211e; --ink: #e2eae6; --muted: #9db0a8; --line: #4b5b55;
    --accent: #78b6de; --acc-soft: #1b3343; --prune: #e3a55c; --prune-soft: #3a2a18; --shade: #212c28;
    color-scheme: dark;
  }}
}}
:root[data-theme="dark"] {{
  --bg: #111715; --surface: #18211e; --ink: #e2eae6; --muted: #9db0a8; --line: #4b5b55;
  --accent: #78b6de; --acc-soft: #1b3343; --prune: #e3a55c; --prune-soft: #3a2a18; --shade: #212c28;
  color-scheme: dark;
}}
* {{ box-sizing: border-box; }}
body {{ background: var(--bg); color: var(--ink); font: 400 16px/1.6 var(--sans); }}
.wrap {{ max-width: 1080px; margin: 0 auto; padding-inline: 20px; padding-block: 48px 80px; }}
header {{ max-width: 760px; }}
.eyebrow {{ font: 500 12px/1 var(--mono); letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }}
h1 {{ font: 800 clamp(34px, 6vw, 58px)/1.02 var(--display); letter-spacing: -.02em; margin: 14px 0 18px; text-wrap: balance; }}
h2 {{ font: 700 clamp(22px, 3vw, 28px)/1.15 var(--display); letter-spacing: -.01em; margin: 0 0 10px; text-wrap: balance; }}
h2 .sec {{ font: 500 14px var(--mono); color: var(--accent); margin-right: 10px; vertical-align: 3px; }}
p {{ max-width: 68ch; margin: 0 0 12px; }}
.lead {{ font-size: 18px; color: var(--muted); }}
code, .mono {{ font-family: var(--mono); font-size: .9em; }}
nav.toc {{ display: flex; flex-wrap: wrap; gap: 8px; margin: 26px 0 8px; }}
nav.toc a {{ font: 500 13px var(--sans); color: var(--ink); text-decoration: none; border: 1px solid var(--line);
  border-radius: 999px; padding: 5px 12px; background: var(--surface); }}
nav.toc a:hover, nav.toc a:focus-visible {{ border-color: var(--accent); color: var(--accent); outline: none; }}
.legend {{ display: flex; flex-wrap: wrap; gap: 18px; margin: 18px 0 0; font-size: 13px; color: var(--muted); }}
.legend span {{ display: inline-flex; align-items: center; gap: 8px; }}
.sw {{ width: 26px; height: 14px; border-radius: 4px; border: 1.4px solid var(--line); background: var(--surface); }}
.sw.a {{ background: var(--acc-soft); border-color: var(--accent); }}
.sw.p {{ background: var(--prune-soft); border-color: var(--prune); border-radius: 8px; }}
section {{ margin-top: 64px; display: grid; gap: 14px; }}
figure {{ margin: 6px 0 0; background: var(--surface); border: 1px solid var(--shade); border-radius: 12px; padding: 18px; }}
.scroll {{ overflow-x: auto; }}
figure svg {{ display: block; width: 100%; height: auto; }}
figcaption {{ font-size: 14px; color: var(--muted); margin-top: 12px; max-width: 80ch; }}
.cards {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); gap: 14px; }}
.card {{ background: var(--surface); border: 1px solid var(--shade); border-radius: 12px; padding: 16px 18px; min-width: 0; }}
.card h3 {{ font: 600 15px var(--sans); margin: 0 0 6px; }}
.card p {{ font-size: 14px; color: var(--muted); margin: 0; }}
table {{ border-collapse: collapse; width: 100%; font-size: 14px; }}
th, td {{ text-align: left; padding: 9px 12px; border-bottom: 1px solid var(--shade); vertical-align: top; }}
th {{ font: 600 12px var(--sans); letter-spacing: .06em; text-transform: uppercase; color: var(--muted); }}
td.num {{ font-family: var(--mono); font-size: 13px; font-variant-numeric: tabular-nums; }}
td.note {{ color: var(--muted); }}
footer {{ margin-top: 64px; font-size: 13px; color: var(--muted); }}

/* SVG vocabulary */
svg text {{ font-family: var(--sans); }}
.t {{ font-size: 13px; font-weight: 600; fill: var(--ink); }}
.s {{ font-size: 11.5px; fill: var(--muted); }}
.m {{ font-family: var(--mono); font-size: 11.5px; fill: var(--ink); }}
.pt {{ font-size: 11.5px; fill: var(--ink); }}
.lb {{ font-size: 11px; fill: var(--muted); }}
.lbp {{ font-size: 11px; font-weight: 600; fill: var(--prune); }}
.lba {{ font-size: 11px; font-weight: 600; fill: var(--accent); }}
.gt {{ font-size: 11px; font-weight: 600; letter-spacing: .06em; fill: var(--muted); }}
.numt {{ font: 600 12px var(--mono); fill: var(--accent); }}
.bx {{ fill: var(--surface); stroke: var(--line); stroke-width: 1.2; }}
.bxa {{ fill: var(--acc-soft); stroke: var(--accent); stroke-width: 1.4; }}
.pill {{ fill: var(--prune-soft); stroke: var(--prune); stroke-width: 1.2; }}
.pilla {{ fill: var(--acc-soft); stroke: var(--accent); }}
.grp {{ fill: none; stroke: var(--line); stroke-width: 1.1; stroke-dasharray: 5 4; }}
.num {{ fill: var(--surface); stroke: var(--accent); stroke-width: 1.3; }}
.ln {{ fill: none; stroke: var(--ink); stroke-width: 1.3; }}
.lnp {{ stroke: var(--prune); }}
.lna {{ stroke: var(--accent); }}
.mk {{ fill: var(--ink); }} .mkp {{ fill: var(--prune); }} .mka {{ fill: var(--accent); }}
.bus {{ stroke: var(--ink); stroke-width: 2.4; }}
.dot {{ fill: var(--accent); }}
.seg-run {{ fill: var(--acc-soft); stroke: var(--accent); stroke-width: 1; }}
.seg-soft {{ stroke: var(--prune); stroke-width: 1; }}
.seg-drop {{ fill: var(--prune-soft); stroke: var(--prune); stroke-width: 1; stroke-dasharray: 3 3; }}
.seg-fin {{ fill: var(--shade); stroke: var(--line); stroke-width: 1; }}
.hatchbg {{ fill: var(--prune-soft); }}
.hatch {{ stroke: var(--prune); stroke-width: 1.4; opacity: .55; }}
.hard {{ stroke: var(--prune); stroke-width: 3; }}
.tick {{ stroke: var(--ink); stroke-width: 1; }}
.chip {{ fill: var(--surface); stroke: var(--line); stroke-width: 1; }}
.cell-on {{ fill: var(--accent); }}
.cell-off {{ fill: none; stroke: var(--line); stroke-width: 1; stroke-dasharray: 3 3; }}
@media (max-width: 560px) {{ .wrap {{ padding-inline: 16px; padding-block: 32px 64px; }} figure {{ padding: 12px; }} }}
</style>

<div class="wrap">
<header>
  <div class="eyebrow">Bitboard Engine 13 · C++ · UCI</div>
  <h1>Inside Bitboard Engine 13</h1>
  <p class="lead">What happens between the GUI's <code>go</code> and the engine's <code>bestmove</code>: the threads,
  the search loop, every decision at one node of the tree, how moves are ordered, how a position is scored,
  and how the clock is spent. Every number here is the one in the source.</p>
  <nav class="toc" aria-label="Sections">
    <a href="#overview">1 The whole engine</a><a href="#board">2 Board and moves</a>
    <a href="#deepening">3 Iterative deepening</a><a href="#node">4 One node</a>
    <a href="#ordering">5 Move ordering</a><a href="#quiescence">6 Quiescence</a>
    <a href="#evaluation">7 Evaluation</a><a href="#time">8 Time</a>
    <a href="#threads">9 Threads and the table</a><a href="#numbers">10 Tuned numbers</a>
  </nav>
  <div class="legend">
    <span><i class="sw"></i>a step</span>
    <span><i class="sw a"></i>shared data, or the result</span>
    <span><i class="sw p"></i>an early return or a cut</span>
  </div>
</header>

<section id="overview">
  <h2><span class="sec">1</span>The whole engine</h2>
  <p>Three kinds of thread. The UCI thread reads commands and is never blocked by a search, so <code>stop</code> and
  <code>isready</code> are answered at once. A search thread runs <code>Search::think()</code>, which starts the helper
  threads and searches as thread 0 itself. All of them read and write one shared transposition table and nothing else.</p>
  <figure><div class="scroll">{fig_overview()}</div>
  <figcaption>The GUI and the engine talk through standard input and output. When thread 0 decides, the helpers are
  told to stop and joined, and the UCI thread prints <code>bestmove</code>.</figcaption></figure>
</section>

<section id="board">
  <h2><span class="sec">2</span>Board and moves</h2>
  <div class="cards">
    <div class="card"><h3>12 bitboards</h3><p>One 64-bit word per piece type and colour. "All white pawns one rank up" is a
    single shift; attack sets come from tables, and sliding pieces use magic multiplication.</p></div>
    <div class="card"><h3>A mailbox beside them</h3><p>A 64-entry array answers "what is on e4?" in one read, kept in step
    with the bitboards on every move.</p></div>
    <div class="card"><h3>A Zobrist key</h3><p>A 64-bit hash, updated by xor on every move. It indexes the table and
    detects repetitions; en passant counts only when the capture is legal.</p></div>
  </div>
  <figure><div class="scroll">{fig_move()}</div>
  <figcaption>Every move is one 16-bit number, so "is it a capture?" and "is it a promotion?" are single bit tests.
  Moves are generated pseudo-legally and checked for legality only when the search reaches them.</figcaption></figure>
</section>

<section id="deepening">
  <h2><span class="sec">3</span>Iterative deepening</h2>
  <p>The engine never searches straight to a target depth. It searches depth 1, then 2, then 3, each time starting from
  what the previous depth stored, which costs less than one blind deep search and always leaves a finished move.</p>
  <figure><div class="scroll">{fig_deepening()}</div>
  <figcaption>If time runs out in the middle of a depth, the engine still plays a root move that depth already proved
  better than the previous best, instead of throwing the partial depth away.</figcaption></figure>
</section>

<section id="node">
  <h2><span class="sec">4</span>One node of the search</h2>
  <p>Every position in the tree goes through the same function, <code>negamax()</code>. Most of its work is deciding
  what <em>not</em> to search: amber exits end the node early, the dashed frames hold the checks that only apply in
  some nodes, and the move loop repeats until a move refutes the opponent (a β-cutoff) or the moves run out.</p>
  <figure><div class="scroll">{node_svg}</div>
  <figcaption>α and β are the window: scores at or below α are too low to matter here, scores at or above β are too
  good for the opponent to allow. d is depth in plies, m the move's number in the loop, eval the corrected static
  evaluation, cp centipawns.</figcaption></figure>
</section>

<section id="ordering">
  <h2><span class="sec">5</span>Move ordering</h2>
  <p>A β-cutoff on the first move saves searching all the others, so the order is worth as much as the pruning. Moves
  come out one at a time, best first, in these bands.</p>
  <figure><div class="scroll">{fig_ordering()}</div>
  <figcaption>Every β-cutoff rewards the move that caused it in the killer, counter, history and continuation tables,
  and penalises the quiet moves tried before it; the scores grow more slowly near their limits, so they stay bounded.
  Each thread keeps its own copies of these tables.</figcaption></figure>
</section>

<section id="quiescence">
  <h2><span class="sec">6</span>Quiescence search</h2>
  <p>At depth zero a position may be in the middle of an exchange, and its evaluation would be wrong by a piece. So the
  engine keeps playing captures until nothing is hanging, then evaluates.</p>
  <figure><div class="scroll">{fig_qsearch()}</div>
  <figcaption>"Stand pat" means the side to move may decline every capture, so the evaluation is a floor. Delta pruning
  skips captures that could not reach α even if the captured piece came free.</figcaption></figure>
</section>

<section id="evaluation">
  <h2><span class="sec">7</span>Evaluation</h2>
  <p>Every term is a pair: one value for the middlegame, one for the endgame. The two totals are mixed by how much
  material is left, so a king that should hide early is pulled to the centre late. Most weights were fitted to the
  results of 1.5 million self-play positions.</p>
  <figure><div class="scroll">{fig_eval()}</div>
  <figcaption>Dots mark which total a term adds to: king safety only counts in the middlegame, the passed-pawn race
  terms only in the endgame. Game phase p is 24 with all pieces on and 0 with none (knight and bishop 1, rook 2, queen 4).
  The table stores scores before the fifty-move fade, since its key does not include the move clock.</figcaption></figure>
</section>

<section id="time">
  <h2><span class="sec">8</span>Time</h2>
  <p>On a clock the engine gives itself a budget B per move and spends more of it while its choice keeps changing.
  The bars are to scale.</p>
  <figure><div class="scroll">{fig_time()}</div>
  <figcaption>Stability is how many depths in a row kept the same best move. With one legal move the engine answers
  at once; during <code>go ponder</code> no limit applies until <code>ponderhit</code>.</figcaption></figure>
</section>

<section id="threads">
  <h2><span class="sec">9</span>Threads and the shared table</h2>
  <p>With several threads (Lazy SMP) every thread searches the same root position. Helpers skip depths on fixed
  patterns, so at any moment the threads are spread over neighbouring depths and fill the table with results the
  others then find.</p>
  <figure><div class="scroll">{fig_smp()}</div>
  <figcaption>Filled cells are searched, dashed ones skipped (Threads = 4). Only thread 0 reports and its move is
  played; the helpers help only through the table.</figcaption></figure>
  <figure><div class="scroll">{fig_tt()}</div>
  <figcaption>Each half of an entry is written in one instruction, but the pair is not, so a reader can catch two
  different writes. Storing the key xored with the data turns that into a harmless miss instead of a wrong answer,
  with no locks.</figcaption></figure>
</section>

<section id="numbers">
  <h2><span class="sec">10</span>Tuned numbers</h2>
  <p>These search margins were tuned by SPSA over 10,000 self-play games: every pair of games nudges each number up
  for one side and down for the other, and moves it towards the side that won.</p>
  <figure><div class="scroll"><table>
    <thead><tr><th>Decision</th><th>Value</th><th>Applies</th></tr></thead>
    <tbody>{rows}</tbody>
  </table></div></figure>
</section>

<footer>Bitboard Engine 13 · source and per-function reference in <code>Engine_13/README.md</code> of
<a href="https://github.com/ChrisKyra/chess-engine" style="color: var(--accent)">ChrisKyra/chess-engine</a>.</footer>
</div>
"""


html = page()
head, body = html.split("</style>", 1)
OUT.write_text('<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n'
               '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
               + head + "</style>\n</head>\n<body>" + body + "</body>\n</html>\n")
print(f"wrote {OUT} ({OUT.stat().st_size // 1024} KB)")
