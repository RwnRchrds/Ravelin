#!/usr/bin/env bash
#
# Play two builds of Ravelin against each other and report the Elo difference.
#
# Ravelin is deterministic, so every game from the starting position would be
# identical. The opening book is what makes a match meaningful, not optional.
#
#   tools/match.sh                          # working tree vs HEAD
#   tools/match.sh --baseline v0.2.0        # working tree vs a tag
#   tools/match.sh --baseline HEAD~1 --rounds 200 --tc 5+0.05
#   tools/match.sh --no-sprt --rounds 100   # fixed length instead of sequential
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FASTCHESS="$REPO_ROOT/tools/bin/fastchess"
BOOK="$REPO_ROOT/tools/books/8moves_v3.pgn"
RESULTS="$REPO_ROOT/tools/results"

BASELINE="HEAD"
TEST="working"
TC="10+0.1"
ROUNDS=500
CONCURRENCY=4
USE_SPRT=1
ELO0=0
ELO1=5

while [[ $# -gt 0 ]]; do
  case "$1" in
    --baseline)    BASELINE="$2"; shift 2 ;;
    --test)        TEST="$2"; shift 2 ;;
    --tc)          TC="$2"; shift 2 ;;
    --rounds)      ROUNDS="$2"; shift 2 ;;
    --concurrency) CONCURRENCY="$2"; shift 2 ;;
    --elo0)        ELO0="$2"; shift 2 ;;
    --elo1)        ELO1="$2"; shift 2 ;;
    --no-sprt)     USE_SPRT=0; shift ;;
    -h|--help)     sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)             echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

[[ -x "$FASTCHESS" ]] || { echo "fastchess missing. Run tools/setup.sh first." >&2; exit 1; }
[[ -f "$BOOK" ]]      || { echo "opening book missing. Run tools/setup.sh first." >&2; exit 1; }

WORKTREES=()
cleanup() {
  for tree in ${WORKTREES[@]+"${WORKTREES[@]}"}; do
    git -C "$REPO_ROOT" worktree remove --force "$tree" 2>/dev/null || true
  done
}
trap cleanup EXIT

# Builds one side and echoes the path to its binary. Build output goes to stderr so
# it cannot contaminate the path this returns.
build_side() {
  local ref="$1" label="$2" root

  if [[ "$ref" == "working" ]]; then
    root="$REPO_ROOT"
    echo "building $label from the working tree" >&2
  else
    root="$(mktemp -d "${TMPDIR:-/tmp%/}/ravelin-$label-XXXXXX")"
    rmdir "$root"
    echo "building $label from $ref" >&2
    git -C "$REPO_ROOT" worktree add --detach -q "$root" "$ref" >&2
    WORKTREES+=("$root")
  fi

  # Historical commits are measured for playing strength, not code style. Their analyser
  # settings are whatever they were at the time, so warnings must not fail the build.
  dotnet build "$root/src/Ravelin.Uci" -c Release -v q --nologo \
    -p:TreatWarningsAsErrors=false -p:EnforceCodeStyleInBuild=false >&2
  echo "$root/src/Ravelin.Uci/bin/Release/net10.0/Ravelin.Uci"
}

BASE_BIN="$(build_side "$BASELINE" base)"
TEST_BIN="$(build_side "$TEST" test)"

mkdir -p "$RESULTS"
STAMP="$(date +%Y%m%d-%H%M%S)"
PGN="$RESULTS/match-$STAMP.pgn"

SPRT_ARGS=()
if [[ "$USE_SPRT" == "1" ]]; then
  # Stop as soon as the result is statistically settled rather than at a fixed count.
  SPRT_ARGS=(-sprt "elo0=$ELO0" "elo1=$ELO1" alpha=0.05 beta=0.05 model=normalized)
fi

echo
echo "baseline : $BASELINE"
echo "test     : $TEST"
echo "tc       : $TC   rounds: $ROUNDS   concurrency: $CONCURRENCY"
echo "pgn      : $PGN"
echo

"$FASTCHESS" \
  -engine "cmd=$TEST_BIN" name=test \
  -engine "cmd=$BASE_BIN" name=base \
  -each "tc=$TC" proto=uci \
  -openings "file=$BOOK" format=pgn order=random \
  -rounds "$ROUNDS" -games 2 -repeat \
  -concurrency "$CONCURRENCY" \
  -maxmoves 200 \
  -ratinginterval 20 \
  -pgnout "file=$PGN" \
  ${SPRT_ARGS[@]+"${SPRT_ARGS[@]}"}
