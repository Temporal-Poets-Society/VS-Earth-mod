#!/bin/bash
# usage: offline.sh <output folder>
# offline checks for pool layout 4: audit + suite, 256k and 1M, layouts 3 and 4
cd "/c/VS mods/Earth"
O="${1:?output folder}"
run() { # tag, env...
  tag=$1; shift
  env "$@" dotnet run -c Release --no-build --project tests/EarthMapTests -- "C:/VS mods/Earth" 384 audit > "$O/audit-$tag.txt" 2>&1
  env "$@" dotnet run -c Release --no-build --project tests/EarthMapTests -- "C:/VS mods/Earth" 384 > "$O/suite-$tag.txt" 2>&1
  echo "$tag done"
}
run 256k-p3 POOLS=3
run 256k-p4 POOLS=4
run 1m-p3 POOLS=3 WORLD=1024000 BANDINNER=672
run 1m-p4 POOLS=4 WORLD=1024000 BANDINNER=672
