# GitHub Actions Workflows

## CI Tests (`ci-tests.yml`)

Automated testing workflow that runs on pull requests to verify code changes.

### Triggers

- **Pull Requests** to `main`, `master`, or `develop` branches
- Only runs when relevant files change (src/, tests/, solution files, or the workflow itself)

### Jobs

#### 1. Unit Tests
- **Runtime**: ~2-5 minutes
- **Dependencies**: None (runs in parallel)
- **Environment**: Ubuntu latest with .NET 10.0 preview
- **Tests**: `SharpAudio.Api.Tests` project
- **Output**: Unit test results uploaded as artifacts

#### 2. Integration Tests
- **Runtime**: ~5-15 minutes (first run with model download), ~3-5 minutes (cached)
- **Dependencies**: Requires unit tests to pass first
- **Environment**: Ubuntu latest with .NET 10.0 preview + espeak-ng
- **Tests**: `SharpAudio.Api.IntegrationTests` project
- **Model Caching**: Uses GitHub Actions cache to persist downloaded models (~1-2GB)
  - Cache key: `model-cache-{config.json hash}-v1`
  - Invalidates automatically when `config.json` changes
- **Process**:
  1. Install system dependencies (espeak-ng)
  2. Restore model cache or download fresh models
  3. Start API service in background
  4. Wait for `/health` endpoint to respond
  5. Run integration tests against live API
  6. Stop API service and upload logs
- **Output**: Integration test results and API service logs uploaded as artifacts

#### 3. Test Report
- **Runtime**: <1 minute
- **Dependencies**: Runs after both test jobs complete
- **Process**: Aggregates test results and publishes a formatted report
- **Reporter**: Uses `dorny/test-reporter` for .NET TRX format
- **Output**: Test summary visible in PR checks and comments

### Test Results

All test runs produce:
- **TRX files**: Standard .NET test result format
- **Artifacts**: Available for 30 days in GitHub Actions
- **Test Report**: Interactive summary in PR checks

### Troubleshooting

#### Integration tests failing to start
- Check `api-service-logs` artifact for startup errors
- Verify model files are downloading correctly
- Ensure espeak-ng is installed properly

#### Cache issues
- Cache key changes with `config.json` modifications
- Manual cache invalidation: Update the `-v1` suffix in cache key to `-v2`
- Cache size limit: GitHub Actions has 10GB total cache per repository

#### Timeout issues
- Unit tests: 10 minute timeout
- Integration tests: 30 minute timeout (allows for model downloads)
- Service health check: 5 minutes (60 attempts × 5 seconds)

### Local Testing

To run tests locally in the same way CI does:

```bash
# Unit tests
dotnet test tests/SharpAudio.Api.Tests/SharpAudio.Api.Tests.csproj

# Integration tests (requires API running)
export MODEL_CACHE_DIR="${PWD}/model-cache"
export ESPEAK_DATA_DIR="/usr/share/espeak-ng-data"

# Start API in background
cd src/SharpAudio.Api
dotnet run &
API_PID=$!
cd ../..

# Wait for health check
sleep 10

# Run tests
dotnet test tests/SharpAudio.Api.IntegrationTests/SharpAudio.Api.IntegrationTests.csproj

# Clean up
kill $API_PID
```

Or use the Docker-based test runner:
```bash
./tests/run-tests.sh all
```

### Performance

- **First run** (cold cache): ~15-20 minutes total
  - Unit tests: ~2 minutes
  - Model download: ~8-12 minutes
  - Integration tests: ~3-5 minutes
  
- **Subsequent runs** (warm cache): ~5-8 minutes total
  - Unit tests: ~2 minutes
  - Integration tests: ~3-5 minutes
  - Model loading from cache: ~30 seconds

### Maintenance

- **Update .NET version**: Modify `dotnet-version` in both unit and integration test jobs
- **Add new test projects**: Add additional `dotnet test` steps to appropriate jobs
- **Modify cache strategy**: Update cache key pattern or add additional paths
- **Adjust timeouts**: Modify `timeout-minutes` if tests consistently exceed limits
