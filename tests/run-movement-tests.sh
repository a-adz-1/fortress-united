#!/bin/sh
# Build and run the differential movement tests (needs the .NET 8 SDK;
# installed here in ~/.dotnet). Compares PlayerMovement against traces
# recorded from the original TFC movement code.
cd "$(dirname "$0")/MovementTests" || exit 1
DOTNET=${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
"$DOTNET" build -nologo -v q | grep -E " error|warning CS" ; "$DOTNET" bin/Debug/net8.0/MovementTests.dll "$@"
