#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

dotnet publish Trader.csproj -c Release
