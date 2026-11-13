#!/bin/bash
# usage: servers.sh <Earth mod build folder>
# server tests for pool layout 4: Niagara in a 1M world and a 256k world
D="$(cd "$(dirname "$0")" && pwd -W)"
cd "/c/VS mods/Earth"
M="${1:?Earth mod build folder}"
R="$(pwd -W)/testruns"
WORLD=1024000 .venv/Scripts/python docs/research/river-drops-test-2026-10-22/mkrun.py "$R/layout4-1m-20261023" 42624 384 "$M" "$D/niagara1m.json"
.venv/Scripts/python docs/research/river-drops-test-2026-10-22/mkrun.py "$R/layout4-256k-20261023" 42625 384 "$M" "$D/niagara256.json"
