#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 6 ]]; then
  echo "Usage: $0 --chain <key> --start-slot <inclusive> --end-slot <inclusive> [--dry-run] [--allow-large-range] [--advance-live-cursor]" >&2
  exit 2
fi

dotnet run --project src/ThisCafeteria.Worker -- --solana-backfill "$@"
