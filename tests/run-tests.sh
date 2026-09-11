#!/bin/bash
set -e

echo "=========================================="
echo "SharpAudio Docker Test Runner"
echo "=========================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Create test results directory
mkdir -p test-results

# Function to run tests
run_tests() {
    local test_type=$1
    local test_project=$2
    
    echo -e "${YELLOW}Running ${test_type} tests...${NC}"
    echo "----------------------------------------"
    
    if docker compose -f docker-compose.test.yml run --rm "${test_project}"; then
        echo -e "${GREEN}✓ ${test_type} tests passed${NC}"
        return 0
    else
        echo -e "${RED}✗ ${test_type} tests failed${NC}"
        return 1
    fi
}

# Parse command line arguments
TEST_TYPE="${1:-all}"
EXIT_CODE=0

case $TEST_TYPE in
    unit)
        echo "Running unit tests only..."
        run_tests "Unit" "unit-tests" || EXIT_CODE=$?
        ;;
    
    integration)
        echo "Running integration tests only..."
        echo "Building and starting SharpAudio service..."
        docker compose -f docker-compose.test.yml up -d fastttsr
        
        echo "Waiting for service to be healthy..."
        sleep 10
        
        run_tests "Integration" "integration-tests" || EXIT_CODE=$?
        
        echo "Stopping services..."
        docker compose -f docker-compose.test.yml down
        ;;
    
    asr-model-tests)
        echo "Running opt-in ASR/TTS circular model tests (real model downloads + real inference)..."
        echo "This does NOT run in CI and is skipped by default; see docs/ASR_IMPLEMENTATION_PLAN.md."
        run_tests "ASR Model" "asr-model-tests" || EXIT_CODE=$?
        ;;
    
    all)
        echo "Running all tests..."
        echo ""
        
        # Run unit tests first
        run_tests "Unit" "unit-tests" || EXIT_CODE=$?
        echo ""
        
        # Run integration tests
        echo "Building and starting SharpAudio service..."
        docker compose -f docker-compose.test.yml up -d fastttsr
        
        echo "Waiting for service to be healthy..."
        sleep 10
        
        run_tests "Integration" "integration-tests" || EXIT_CODE=$?
        
        echo "Stopping services..."
        docker compose -f docker-compose.test.yml down
        ;;
    
    *)
        echo -e "${RED}Invalid test type: $TEST_TYPE${NC}"
        echo "Usage: $0 [unit|integration|asr-model-tests|all]"
        exit 1
        ;;
esac

echo ""
echo "=========================================="
if [ $EXIT_CODE -eq 0 ]; then
    echo -e "${GREEN}All tests completed successfully!${NC}"
else
    echo -e "${RED}Some tests failed. Exit code: $EXIT_CODE${NC}"
fi
echo "=========================================="

exit $EXIT_CODE
