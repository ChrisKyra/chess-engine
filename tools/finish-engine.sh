#!/bin/bash
# Finishes an engine version: SPSA-tunes its search parameters, checks the tuned
# values against the untuned ones, and measures the result against the previous
# engine. Every stage logs to <engine>/finish/ and can be run on its own.
#
#   tools/finish-engine.sh ENGINE_DIR BASELINE_ENGINE [stage...]
#
#   ENGINE_DIR       the engine folder (with spsa_params.txt and apply_spsa.py)
#   BASELINE_ENGINE  the previous engine's binary, for the final match
#   stages           any of: build check spsa apply verify final   (default: all, in order)
#
#   build   make the engine and the SPSA build (make spsa)
#   check   a short SPSA run (8 games at 1 s + 0.01 s) to make sure the setup works
#   spsa    the tuning run: SPSA_GAMES games (default 10000) at SPSA_TC (default 2+0.02),
#           CONCURRENCY at once (default 8); values saved to finish/tuned_params.txt
#   apply   keep a copy of the untuned engine, write the tuned values into search.cpp,
#           rebuild both builds (so engine-spsa starts from the tuned values too) and
#           run the perft suite
#   verify  tuned against untuned: SPRT [0, 10], at most 250 games at 8 s + 0.08 s
#   final   the engine against BASELINE_ENGINE: 400 games at 8 s + 0.08 s, no early stop
#
# All matches use one thread per engine and 64 MB of hash.
set -euo pipefail

ENGINE_DIR="$(cd "$1" && pwd)"
BASELINE="$2"
shift 2
STAGES=("$@")
[ ${#STAGES[@]} -eq 0 ] && STAGES=(build check spsa apply verify final)

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RUNNER_DIR="$ROOT/tools/match-runner"
RUNNER="$RUNNER_DIR/bin/Release/net10.0/match-runner"
OUT="$ENGINE_DIR/finish"
SPSA_GAMES="${SPSA_GAMES:-10000}"
SPSA_TC="${SPSA_TC:-2+0.02}"
CONCURRENCY="${CONCURRENCY:-8}"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
mkdir -p "$OUT"

[ -x "$RUNNER" ] || dotnet build -c Release -v q "$RUNNER_DIR"

stage() { echo "=== $1 $(date +%H:%M)"; }

for s in "${STAGES[@]}"; do
  case "$s" in
    build)
      stage build
      make -C "$ENGINE_DIR" -s && make -C "$ENGINE_DIR" -s spsa
      "$ENGINE_DIR/engine" test | tail -1
      ;;
    check)
      stage check
      "$RUNNER" --engine1 "$ENGINE_DIR/engine-spsa" --spsa "$ENGINE_DIR/spsa_params.txt" \
        --games 8 --tc 1+0.01 --concurrency 4 --option Threads=1 --option Hash=16 --report 2 \
        | tee "$OUT/spsa_check.log" | tail -20
      ;;
    spsa)
      stage "spsa ($SPSA_GAMES games at $SPSA_TC)"
      "$RUNNER" --engine1 "$ENGINE_DIR/engine-spsa" --spsa "$ENGINE_DIR/spsa_params.txt" \
        --spsa-out "$OUT/tuned_params.txt" --games "$SPSA_GAMES" --tc "$SPSA_TC" \
        --concurrency "$CONCURRENCY" --option Threads=1 --option Hash=16 --report 200 \
        > "$OUT/spsa.log" 2>&1
      tail -22 "$OUT/spsa.log"
      ;;
    apply)
      stage apply
      cp "$ENGINE_DIR/engine" "$OUT/engine-untuned"
      cp "$ENGINE_DIR/search.cpp" "$OUT/search-untuned.cpp"
      python3 "$ENGINE_DIR/apply_spsa.py" "$OUT/tuned_params.txt" "$ENGINE_DIR/search.cpp"
      make -C "$ENGINE_DIR" -s && make -C "$ENGINE_DIR" -s spsa
      "$ENGINE_DIR/engine" test | tail -1 && "$ENGINE_DIR/engine" bench | tail -1
      ;;
    verify)
      stage "verify (tuned vs untuned)"
      "$RUNNER" --engine1 "$ENGINE_DIR/engine" --engine2 "$OUT/engine-untuned" --tc 8+0.08 --games 250 \
        --concurrency "$CONCURRENCY" --sprt 0,10 --option Threads=1 --option Hash=64 \
        --pgn "$OUT/verify.pgn" --report 50 > "$OUT/verify.log" 2>&1
      tail -5 "$OUT/verify.log"
      ;;
    final)
      stage "final (against $BASELINE)"
      "$RUNNER" --engine1 "$ENGINE_DIR/engine" --engine2 "$BASELINE" --tc 8+0.08 --games 400 \
        --concurrency "$CONCURRENCY" --no-sprt --option Threads=1 --option Hash=64 \
        --pgn "$OUT/final.pgn" --report 50 > "$OUT/final.log" 2>&1
      tail -5 "$OUT/final.log"
      ;;
    *) echo "unknown stage: $s"; exit 1 ;;
  esac
done
