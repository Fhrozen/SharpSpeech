# Development Guide

This guide will help you set up a local development environment for FastTTSR.

## Prerequisites

### Required Software

- **.NET 10 SDK (Preview)** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Node.js 24+** - [Download](https://nodejs.org/)
- **Docker & Docker Compose** - [Download](https://www.docker.com/products/docker-desktop)
- **Git** - [Download](https://git-scm.com/)

### Optional Tools

- **Visual Studio Code** with C# and Vue extensions
- **Visual Studio 2022** (17.13+)
- **JetBrains Rider**
- **Postman** or **Insomnia** for API testing

### System Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| RAM | 4 GB | 8 GB |
| CPU | 2 cores | 4 cores |
| Disk Space | 10 GB | 20 GB |
| OS | Windows 10, macOS 11, Linux | Latest versions |

---

## Quick Start

### Clone the Repository

```bash
git clone https://github.com/yourusername/FastTTSR.git
cd FastTTSR
```

### Run with Docker (Easiest)

```bash
# Start all services
docker compose up -d

# View logs
docker compose logs -f

# Stop services
docker compose down
```

Access the application:
- **Web UI:** http://localhost:5768
- **API:** http://localhost:5768/v1/audio/speech
- **Swagger:** http://localhost:5768/swagger

---

## Local Development Setup

### Backend Development

#### 1. Install Dependencies

```bash
cd src/FastTTSR.Api
dotnet restore
```

#### 2. Install espeak-ng (Required for Kokoro)

**Ubuntu/Debian:**
```bash
sudo apt-get update
sudo apt-get install -y libespeak-ng1 espeak-ng-data
```

**macOS:**
```bash
brew install espeak-ng
```

**Windows:**
```powershell
# Using Chocolatey
choco install espeak-ng

# Or download from: https://github.com/espeak-ng/espeak-ng/releases
```

#### 3. Set Environment Variables

**Linux/macOS:**
```bash
export MODEL_CACHE_DIR=./model-cache
export ESPEAK_DATA_DIR=/usr/share/espeak-ng-data
export MODEL_IDLE_TIMEOUT_SECONDS=0  # Keep models in memory
```

**Windows (PowerShell):**
```powershell
$env:MODEL_CACHE_DIR=".\model-cache"
$env:ESPEAK_DATA_DIR="C:\Program Files\eSpeak NG\espeak-ng-data"
$env:MODEL_IDLE_TIMEOUT_SECONDS=0
```

#### 4. Create Required Directories

```bash
mkdir -p model-cache
mkdir -p assets
```

#### 5. Run the Backend

```bash
cd src/FastTTSR.Api
dotnet run
```

The API will start on http://localhost:5000 (or port specified in launchSettings.json).

#### 6. Verify Backend

```bash
# Health check
curl http://localhost:5000/health

# List models
curl http://localhost:5000/api/models

# Test synthesis
curl -X POST http://localhost:5000/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-q4","input":"Test","voice":"af"}' \
  --output test.wav
```

### Frontend Development

#### 1. Install pnpm

```bash
corepack enable pnpm
```

Or install globally:
```bash
npm install -g pnpm
```

#### 2. Install Dependencies

```bash
cd frontend
pnpm install
```

#### 3. Configure API Endpoint

Create `frontend/.env.local`:
```env
VITE_API_URL=http://localhost:5000
```

#### 4. Run Development Server

```bash
pnpm dev
```

The frontend will start on http://localhost:5173 with hot module replacement.

#### 5. Build for Production

```bash
pnpm build
```

Output will be in `frontend/dist/`.

---

## Project Structure

```
FastTTSR/
├── src/
│   └── FastTTSR.Api/              # Backend API
│       ├── Program.cs             # Entry point & endpoint definitions
│       ├── appsettings.json       # Configuration
│       ├── config.json            # Model definitions
│       ├── Contracts/             # Request/response DTOs
│       ├── Models/                # Domain models
│       ├── Options/               # Configuration options
│       └── Services/              # Business logic
│           ├── ModelCatalog.cs    # Model registry
│           ├── ModelCache.cs      # Model lifecycle
│           ├── KokoroTtsSynthesizer.cs
│           ├── SupertonicTtsSynthesizer.cs
│           ├── TextSanitizer.cs
│           └── Phonemization/
│               └── EspeakWrapper.cs
│
├── frontend/                      # Vue.js frontend
│   ├── src/
│   │   ├── App.vue               # Main component
│   │   ├── main.js               # Entry point
│   │   ├── types.ts              # TypeScript types
│   │   ├── preset-texts.ts       # Sample texts
│   │   └── components/           # Vue components
│   ├── package.json
│   ├── vite.config.js
│   └── tsconfig.json
│
├── tests/                         # Test suites
│   ├── FastTTSR.Api.Tests/       # Unit tests
│   └── FastTTSR.Api.IntegrationTests/  # Integration tests
│
├── model-cache/                   # Downloaded models (gitignored)
├── assets/                        # Static assets
│   ├── tokens.txt
│   └── espeak-ng-data/
│
├── docs/                          # Documentation
├── docker-compose.yml
├── Dockerfile
└── FastTTSR.slnx                 # Solution file
```

---

## Development Workflow

### 1. Create a Feature Branch

```bash
git checkout -b feature/my-feature
```

### 2. Make Changes

Edit code in your IDE. The development server will auto-reload on changes.

**Hot Reload:**
- Backend: Use `dotnet watch run` for auto-reload
- Frontend: `pnpm dev` has hot module replacement

### 3. Run Tests

```bash
# Backend unit tests
dotnet test tests/FastTTSR.Api.Tests

# Backend integration tests
dotnet test tests/FastTTSR.Api.IntegrationTests

# All tests
dotnet test

# Frontend tests (if implemented)
cd frontend
pnpm test
```

### 4. Format Code

**Backend:**
```bash
dotnet format
```

**Frontend:**
```bash
cd frontend
pnpm format  # If configured
```

### 5. Commit Changes

```bash
git add .
git commit -m "feat: add my feature"
```

**Commit Message Convention:**
- `feat:` - New feature
- `fix:` - Bug fix
- `docs:` - Documentation changes
- `refactor:` - Code refactoring
- `test:` - Adding tests
- `chore:` - Maintenance tasks

### 6. Push and Create PR

```bash
git push origin feature/my-feature
```

Then create a pull request on GitHub.

---

## Debugging

### Backend Debugging

#### Visual Studio Code

Create `.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": ".NET Core Launch (web)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/FastTTSR.Api/bin/Debug/net10.0/FastTTSR.Api.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/FastTTSR.Api",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "MODEL_CACHE_DIR": "${workspaceFolder}/model-cache",
        "MODEL_IDLE_TIMEOUT_SECONDS": "0"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/Views"
      }
    }
  ]
}
```

Set breakpoints and press F5 to start debugging.

#### Visual Studio 2022

1. Open `FastTTSR.slnx`
2. Set `FastTTSR.Api` as startup project
3. Press F5 to debug

### Frontend Debugging

#### Browser DevTools

1. Open http://localhost:5173
2. Press F12 for DevTools
3. Use Console, Network, and Sources tabs

#### VS Code with Chrome Debugger

Install "Debugger for Chrome" extension, then create `.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "chrome",
      "request": "launch",
      "name": "Launch Chrome against localhost",
      "url": "http://localhost:5173",
      "webRoot": "${workspaceFolder}/frontend/src"
    }
  ]
}
```

### Docker Debugging

View logs:
```bash
docker compose logs -f fastttsr
```

Enter container:
```bash
docker compose exec fastttsr /bin/bash
```

Debug inside container:
```bash
# Check espeak-ng
espeak-ng --voices

# Check model cache
ls -la /cache

# Test API from inside
curl http://localhost:5768/health
```

---

## Testing

### Unit Tests

Located in `tests/FastTTSR.Api.Tests/`

**Run all unit tests:**
```bash
dotnet test tests/FastTTSR.Api.Tests
```

**Run specific test:**
```bash
dotnet test tests/FastTTSR.Api.Tests --filter "FullyQualifiedName~KokoroMetadataTests"
```

**Example test:**
```csharp
[Fact]
public void TryNormalizeLanguage_EnglishAliases_ReturnsEnUs()
{
    Assert.True(KokoroMetadata.TryNormalizeLanguage("en", out var result));
    Assert.Equal("en-us", result);
    
    Assert.True(KokoroMetadata.TryNormalizeLanguage("a", out result));
    Assert.Equal("en-us", result);
}
```

### Integration Tests

Located in `tests/FastTTSR.Api.IntegrationTests/`

**Run integration tests:**
```bash
dotnet test tests/FastTTSR.Api.IntegrationTests
```

**Example integration test:**
```csharp
[Fact]
public async Task SynthesizeSpeech_ValidRequest_ReturnsWavAudio()
{
    var request = new
    {
        model = "kokoro-q4",
        input = "Hello world",
        voice = "af"
    };

    var response = await _client.PostAsJsonAsync("/v1/audio/speech", request);
    
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
}
```

### Test Coverage

Generate coverage report:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

View coverage with ReportGenerator:
```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage -reporttypes:Html
```

Open `coverage/index.html` in browser.

---

## Adding New Features

### Adding a New TTS Model

1. **Add model definition** to `config.json`:
```json
{
  "name": "my-model",
  "engine": "kokoro",
  "modelPath": "onnx/my_model.onnx",
  "assets": [...]
}
```

2. **Implement engine** (if new engine type):
```csharp
public class MyModelSynthesizer : ITtsSynthesizer, IIdleTrackingSynthesizer
{
    public async Task<SynthesisResult> SynthesizeAsync(...)
    {
        // Implementation
    }
}
```

3. **Register in DI** (`Program.cs`):
```csharp
builder.Services.AddSingleton<MyModelSynthesizer>();
```

4. **Update router** (`TtsSynthesizerRouter.cs`):
```csharp
if (model.Engine == "my-engine")
{
    return await _myModelSynthesizer.SynthesizeAsync(...);
}
```

### Adding a New API Endpoint

Add to `Program.cs`:
```csharp
app.MapGet("/api/my-endpoint", () => 
{
    return Results.Ok(new { message = "Hello" });
})
    .WithName("MyEndpoint")
    .WithTags("Custom")
    .WithSummary("My custom endpoint")
    .Produces<object>(200);
```

### Adding a New Frontend Component

Create `frontend/src/components/MyComponent.vue`:
```vue
<script setup lang="ts">
import { ref } from 'vue'

const message = ref('Hello')
</script>

<template>
  <div class="my-component">
    {{ message }}
  </div>
</template>

<style scoped>
.my-component {
  padding: 1rem;
}
</style>
```

Use in `App.vue`:
```vue
<script setup>
import MyComponent from './components/MyComponent.vue'
</script>

<template>
  <MyComponent />
</template>
```

---

## Common Development Tasks

### Reset Model Cache

```bash
rm -rf model-cache/*
```

Models will be re-downloaded on next startup.

### Update Dependencies

**Backend:**
```bash
dotnet list package --outdated
dotnet add package Microsoft.ML.OnnxRuntime --version x.x.x
```

**Frontend:**
```bash
cd frontend
pnpm update
pnpm outdated
```

### Build Docker Image Locally

```bash
docker build -t fastttsr:local .
```

Run local image:
```bash
docker run -p 5768:5768 \
  -v $(pwd)/model-cache:/cache \
  -v $(pwd)/assets:/app/assets:ro \
  fastttsr:local
```

### Run with Different Ports

```bash
# Backend
HTTP_PORT=8080 dotnet run

# Frontend
PORT=3000 pnpm dev

# Docker
HOST_PORT=8080 HTTP_PORT=8080 docker compose up
```

### Enable Detailed Logging

```bash
Logging__LogLevel__Default=Debug \
Logging__LogLevel__Microsoft.AspNetCore=Information \
dotnet run
```

---

## Performance Profiling

### dotnet-trace

```bash
# Install tool
dotnet tool install -g dotnet-trace

# Start profiling
dotnet-trace collect --process-id $(pidof FastTTSR.Api)

# Analyze with PerfView or speedscope.app
```

### BenchmarkDotNet

Add to test project:
```csharp
[MemoryDiagnoser]
public class SynthesisBenchmarks
{
    [Benchmark]
    public async Task SynthesizeShortText()
    {
        // Benchmark code
    }
}
```

Run:
```bash
dotnet run -c Release --project tests/FastTTSR.Api.Benchmarks
```

---

## Troubleshooting Development Issues

### espeak-ng Not Found

**Error:** `Unable to load shared library 'libespeak-ng.so.1'`

**Solution:**
```bash
# Linux
sudo apt-get install libespeak-ng1

# macOS
brew install espeak-ng

# Check installation
which espeak-ng
```

### Model Download Fails

**Error:** `Failed to download model from Hugging Face`

**Solution:**
- Check internet connection
- Verify Hugging Face is accessible
- Check disk space
- Manually download and place in `model-cache/`

### Frontend Can't Connect to API

**Error:** `Network request failed`

**Solution:**
- Verify backend is running
- Check `VITE_API_URL` in `.env.local`
- Check CORS settings in `Program.cs`
- Disable browser extensions (ad blockers)

### High Memory Usage

**Solution:**
- Set `MODEL_IDLE_TIMEOUT_SECONDS=60`
- Reduce number of loaded models
- Use quantized models (kokoro-q4)
- Monitor with `dotnet-counters`

---

## Contributing Guidelines

1. **Fork the repository**
2. **Create a feature branch** from `main`
3. **Write tests** for new features
4. **Ensure all tests pass**
5. **Update documentation** if needed
6. **Follow code style** (use `dotnet format`)
7. **Write clear commit messages**
8. **Create a pull request** with description

### Code Style

- Use C# naming conventions (PascalCase for public, camelCase for private)
- Keep methods focused and small
- Add XML documentation comments for public APIs
- Use `async`/`await` for I/O operations
- Prefer dependency injection over static methods

### Pull Request Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual testing performed

## Checklist
- [ ] Code follows style guidelines
- [ ] Self-review completed
- [ ] Documentation updated
- [ ] No new warnings introduced
```

---

## Resources

### Documentation
- [ASP.NET Core Docs](https://docs.microsoft.com/aspnet/core/)
- [Vue.js Guide](https://vuejs.org/guide/)
- [ONNX Runtime Docs](https://onnxruntime.ai/docs/)
- [espeak-ng Documentation](https://github.com/espeak-ng/espeak-ng/blob/master/docs/index.md)

### Tools
- [Swagger Editor](https://editor.swagger.io/)
- [Postman](https://www.postman.com/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [Visual Studio Code](https://code.visualstudio.com/)

### Community
- [GitHub Issues](https://github.com/yourusername/FastTTSR/issues)
- [Discussions](https://github.com/yourusername/FastTTSR/discussions)
