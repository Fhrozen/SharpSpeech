#!/bin/bash
# Universal .NET CLI wrapper using Docker
# Usage: ./dotnet.sh [any dotnet command]
# Examples:
#   ./dotnet.sh restore
#   ./dotnet.sh build
#   ./dotnet.sh test
#   ./dotnet.sh run --project src/SharpAudio.Api
#   ./dotnet.sh publish -c Release

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
DOTNET_VERSION="10.0-preview"
IMAGE="mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}"

# Get current user IDs to avoid permission issues
USER_ID=$(id -u)
GROUP_ID=$(id -g)

# Create local cache directories if they don't exist
CACHE_DIR=".docker-cache"
mkdir -p "${CACHE_DIR}/nuget"
mkdir -p "${CACHE_DIR}/dotnet"
mkdir -p "${CACHE_DIR}/dotnet/shm"  # Create shm directory for mutex files

# Show what we're running
echo -e "${BLUE}🐳 Running: dotnet $@${NC}"
echo ""

# Run dotnet command in container with persistent caches
docker run --rm \
  --user "${USER_ID}:${GROUP_ID}" \
  -v "$(pwd):/workspace" \
  -v "$(pwd)/${CACHE_DIR}/nuget:/tmp/.nuget" \
  -v "$(pwd)/${CACHE_DIR}/dotnet:/tmp/.dotnet" \
  -w /workspace \
  -e DOTNET_CLI_HOME=/tmp/.dotnet \
  -e HOME=/tmp \
  -e NUGET_PACKAGES=/tmp/.nuget/packages \
  -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  "${IMAGE}" \
  dotnet "$@"

EXIT_CODE=$?

echo ""
if [ $EXIT_CODE -eq 0 ]; then
    echo -e "${GREEN}✅ Command completed successfully${NC}"
else
    echo -e "${RED}❌ Command failed with exit code $EXIT_CODE${NC}"
    exit $EXIT_CODE
fi
