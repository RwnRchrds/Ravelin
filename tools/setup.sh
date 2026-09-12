#!/usr/bin/env bash
#
# Fetches everything tools/match.sh needs: the fastchess match runner and an
# opening book. Both are gitignored, so this is a one-time setup per checkout.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BOOK_URL="https://github.com/official-stockfish/books/raw/master/8moves_v3.pgn.zip"

mkdir -p "$REPO_ROOT/tools/bin" "$REPO_ROOT/tools/books"

if [[ -x "$REPO_ROOT/tools/bin/fastchess" ]]; then
  echo "fastchess: already present"
else
  echo "fastchess: building from source"
  build_dir="$(mktemp -d)"
  git clone -q --depth 1 https://github.com/Disservin/fastchess.git "$build_dir/fastchess"
  make -C "$build_dir/fastchess" -j"$(getconf _NPROCESSORS_ONLN)" >/dev/null
  cp "$build_dir/fastchess/fastchess" "$REPO_ROOT/tools/bin/"
  rm -rf "$build_dir"
fi

if [[ -f "$REPO_ROOT/tools/books/8moves_v3.pgn" ]]; then
  echo "opening book: already present"
else
  echo "opening book: downloading"
  zip="$(mktemp)"
  curl -sSL --max-time 180 -o "$zip" "$BOOK_URL"
  unzip -o -q "$zip" -d "$REPO_ROOT/tools/books"
  rm -f "$zip"
fi

command -v stockfish >/dev/null \
  && echo "stockfish: $(command -v stockfish)" \
  || echo "stockfish: not found (only needed by tools/vs-stockfish.sh)"

echo "ready: tools/match.sh"
