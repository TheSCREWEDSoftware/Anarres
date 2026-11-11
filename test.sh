#!/bin/sh
# Runs the core tests and the standards check on unpushed commits
cd "$(dirname "$0")" || exit 2
failed=""
dotnet run --project tests/Anarres.Core.Tests -c Release || failed="$failed core"
dotnet run --project tools/Anarres.Standards -v q -- --unpushed || failed="$failed standards"
if [ -n "$failed" ]; then
  echo "FAILED:$failed"
  exit 1
fi
echo "Everything passed."
