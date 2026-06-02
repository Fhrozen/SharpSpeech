# FastTTSR

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Vue.js](https://img.shields.io/badge/Vue.js-3.5-4FC08D?logo=vue.js)](https://vuejs.org/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker)](https://www.docker.com/)
[![OpenAI Compatible](https://img.shields.io/badge/OpenAI-Compatible-00A67E)](https://platform.openai.com/docs/api-reference/audio/createSpeech)

**FastTTSR** is a high-performance Text-to-Speech REST API with OpenAI-compatible endpoints, **highly vibe-coded**:blush:. Built with .NET 10, it features a Vue.js frontend, Docker containerization, and support for multiple state-of-the-art TTS models including Kokoro and Supertonic-3.

## ✨ Features

- 🎯 **OpenAI-Compatible API** - Drop-in replacement for OpenAI's `/v1/audio/speech` endpoint
- 🚀 **High Performance** - Custom ONNX inference engine with direct OnnxRuntime integration
- 💾 **Worker Process Architecture** - OS-level memory isolation with automatic cleanup and guaranteed resource reclamation
- 🌍 **Multilingual** - Support for 3+ languages including English, Japanese, Chinese, Spanish, French, and more
- 🎨 **Multiple TTS Models** - Kokoro (quantized & full precision) and Supertonic-3 models
- 🎭 **Rich Voice Library** - 5+ speaker voices with diverse characteristics
- 🔧 **Production Ready** - Built-in health checks, automatic model management, and graceful degradation
- 📊 **Interactive Docs** - Swagger/OpenAPI documentation included
- 🎨 **Modern UI** - Vue.js frontend with real-time audio preview and metrics
- 🐳 **Docker Native** - Multi-stage builds with optimized containerization
- 🧵 **Thread-Safe** - Advanced concurrency handling for multi-tenant deployments

## 🚀 Quick Start

### Using Docker Compose (Recommended)

```bash
# Clone the repository
git clone https://github.com/yourusername/FastTTSR.git
cd FastTTSR

# Start the service
docker compose up -d

# Access the web interface
open http://localhost:5768

# Or use the API directly
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "Hello world, this is a test of FastTTSR.",
    "voice": "af_bella",
    "speed": 1.0
  }' \
  --output speech.wav
```

The service will automatically download required models from Hugging Face on first startup.

## 📚 Documentation

- **[Architecture](docs/ARCHITECTURE.md)** - System design and components
- **[API Reference](docs/API.md)** - Detailed endpoint documentation
- **[Configuration](docs/CONFIGURATION.md)** - Environment variables and settings
- **[Development Guide](docs/DEVELOPMENT.md)** - Local development setup
- **[Deployment Guide](docs/DEPLOYMENT.md)** - Production deployment strategies
- **[Models](docs/MODELS.md)** - TTS models and voice documentation
- **[Troubleshooting](docs/TROUBLESHOOTING.md)** - Common issues and solutions
- **[Text Sanitization](docs/TEXT_SANITIZATION.md)** - Thread safety and text processing

## 🎯 Core Technologies

- **Backend**: .NET 10 with ASP.NET Core Minimal APIs
- **Frontend**: Vue.js 3.5 with TypeScript
- **TTS Engine**: Microsoft.ML.OnnxRuntime + espeak-ng
- **Models**: Kokoro-82M (ONNX) & Supertonic-3 (ONNX)
- **Containerization**: Docker with multi-stage builds
- **Build Tool**: pnpm for frontend dependencies

## 🔌 API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check endpoint |
| `/api/models` | GET | List available models with details |
| `/v1/models` | GET | OpenAI-compatible model listing |
| `/v1/audio/speech` | POST | OpenAI-compatible speech synthesis |
| `/swagger` | GET | Interactive API documentation |

## 🎭 Supported Models

| Model | Description | Languages | Voices | Quality |
|-------|-------------|-----------|--------|---------|
| `kokoro-q4` | Quantized Kokoro 82M | 3+ | 5+ | Fast, low memory |
| `kokoro-full` | Full precision Kokoro 82M | 3+ | 5+ | High quality |
| `supertonic-3` | Supertonic multilingual | 3+ | 10 styles | Production grade |

See [Models Documentation](docs/MODELS.md) for detailed information about each model.

## 🌍 Supported Languages

English (US/GB), Spanish, French, Hindi, Italian, Japanese, Portuguese (BR), Chinese (CN), Korean, German, Dutch, Arabic, Russian, Turkish, Polish, Swedish, Danish, Norwegian, Finnish, Greek, Czech, Romanian, Hungarian, Thai, Vietnamese, Indonesian, Hebrew, Ukrainian, and more.

## ⚙️ Configuration

Key environment variables:

```bash
HTTP_PORT=5768                        # HTTP port
MODEL_CACHE_DIR=/cache                # Model cache directory
ESPEAK_DATA_DIR=/app/assets/espeak-ng-data  # espeak-ng data
MODEL_IDLE_TIMEOUT_SECONDS=60         # Model unload timeout
```

See [Configuration Guide](docs/CONFIGURATION.md) for all options.

## 🛠️ Development

### Local Development (without Docker)

```bash
# Backend
cd src/FastTTSR.Api
dotnet restore
dotnet run

# Frontend
cd frontend
corepack enable pnpm
pnpm install
pnpm dev
```

See [Development Guide](docs/DEVELOPMENT.md) for detailed setup instructions.

## 🧪 Testing

```bash
# Run all tests
./tests/run-tests.sh

# Run specific test suite
dotnet test tests/FastTTSR.Api.Tests
dotnet test tests/FastTTSR.Api.IntegrationTests
```

## 📦 Production Deployment

FastTTSR is production-ready with:
- Automatic model memory management
- Graceful degradation on failures
- Health check endpoints
- Request validation and error handling
- Thread-safe concurrent request processing

See [Deployment Guide](docs/DEPLOYMENT.md) for production deployment strategies.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📄 License

See [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) - TTS model
- [Supertonic-3](https://huggingface.co/Supertone/supertonic-3) - Multilingual TTS model
- [espeak-ng](https://github.com/espeak-ng/espeak-ng) - Phonemization engine
- [ONNX Runtime](https://onnxruntime.ai/) - Inference engine

## 📞 Support

- Documentation: [docs/](docs/)
- Issues: [GitHub Issues](https://github.com/yourusername/FastTTSR/issues)
- API Docs: http://localhost:5768/swagger (when running)
- Configure the timeout via the `MODEL_IDLE_TIMEOUT_SECONDS` environment variable
- Set to `0` to disable automatic release and keep all loaded models in memory

## Tests

### Local Testing

Unit tests:
```bash
dotnet test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj
```

Integration tests with Docker:
```bash
# Run all tests (unit + integration)
./tests/run-tests.sh all

# Run specific test types
./tests/run-tests.sh unit
./tests/run-tests.sh integration
```

Or use Docker Compose directly:
```bash
docker compose -f docker-compose.test.yml up --abort-on-container-exit
```

### Continuous Integration

GitHub Actions automatically runs tests on all pull requests:

- **Unit Tests**: Fast, isolated tests (~2 minutes)
- **Integration Tests**: Full API tests with model downloads (~5-15 minutes)
- **Test Reports**: Automated test summaries in PR checks

The CI workflow:
- Runs on PRs to `main`, `master`, or `develop` branches
- Caches model files (~1-2GB) to speed up subsequent runs
- Uploads test results and API logs as artifacts
- Reports test failures directly in PR checks

See [`.github/workflows/README.md`](.github/workflows/README.md) for details.

## Architecture

```
HTTP Request → KokoroTtsSynthesizer
                    ↓
              KokoroTtsEngine
                    ↓
        ┌───────────┴───────────┐
        ↓                       ↓
  EspeakWrapper          OnnxRuntime
  (phonemization)        (inference)
        ↓                       ↓
  libespeak-ng.so.1      model.onnx
        └───────────┬───────────┘
                    ↓
            PCM16 WAV Output
```

## Requirements

- .NET 10.0 SDK (preview)
- Docker with Compose v2
- Node.js 24+ with pnpm (for frontend development)
- espeak-ng library (included in Docker image)
