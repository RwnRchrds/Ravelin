#!/usr/bin/env bash
#
# Play Ravelin against Stockfish at a capped strength, to anchor it against an
# absolute rating rather than against its own previous version.
#
# Stockfish at full strength would win every game and tell you nothing. Start at
# the floor (1320) and raise it once Ravelin is scoring above about 60%.
#
#   tools/vs-stockfish.sh                 # 1320 Elo, 50 game pairs
#   tools/vs-stockfish.sh --elo 1600 --rounds 100
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FASTCHESS="$REPO_ROOT/tools/bin/fastchess"
BOOK="$REPO_ROOT/tools/books/8moves_v3.pgn"
RESULTS="$REPO_ROOT/tools/results"
RAVELIN="$REPO_ROOT/src/Ravelin.Uci/bin/Release/net10.0/Ravelin.Uci"

ELO=1320
TC="10+0.1"
ROUNDS=50
CONCURRENCY=4

while [[ $# -gt 0 ]]; do
  case "$1" in
    --elo)         ELO="$2"; shift 2 ;;
    --tc)          TC="$2"; shift 2 ;;
    --rounds)      ROUNDS="$2"; shift 2 ;;
    --concurrency) CONCURRENCY="$2"; shift 2 ;;
    -h|--help)     sed -n '2,11p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)             echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

[[ -x "$FASTCHESS" ]] || { echo "fastchess missing. Run tools/setup.sh first." >&2; exit 1; }
[[ -f "$BOOK" ]]      || { echo "opening book missing. Run tools/setup.sh first." >&2; exit 1; }
command -v stockfish >/dev/null || { echo "stockfish not on PATH. brew install stockfish" >&2; exit 1; }

echo "building Ravelin from the working tree" >&2
dotnet build "$REPO_ROOT/src/Ravelin.Uci" -c Release -v q --nologo >&2

mkdir -p "$RESULTS"
PGN="$RESULTS/stockfish-$ELO-$(date +%Y%m%d-%H%M%S).pgn"

echo
echo "opponent : Stockfish capped at $ELO"
echo "tc       : $TC   rounds: $ROUNDS   concurrency: $CONCURRENCY"
echo "pgn      : $PGN"
echo

"$FASTCHESS" \
  -engine "cmd=$RAVELIN" name=Ravelin \
  -engine cmd=stockfish "name=Stockfish-$ELO" \
      option.UCI_LimitStrength=true "option.UCI_Elo=$ELO" option.Threads=1 option.Hash=16 \
  -each "tc=$TC" proto=uci \
  -openings "file=$BOOK" format=pgn order=random \
  -rounds "$ROUNDS" -games 2 -repeat \
  -concurrency "$CONCURRENCY" \
  -maxmoves 200 \
  -ratinginterval 10 \
  -pgnout "file=$PGN"
