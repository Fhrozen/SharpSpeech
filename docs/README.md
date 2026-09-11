# SharpAudio Documentation

Welcome to the SharpAudio documentation! This guide will help you get started with SharpAudio, a high-performance Text-to-Speech REST API with OpenAI-compatible endpoints.

## 📚 Documentation Index

### Getting Started

- **[README](../README.md)** - Project overview, quick start, and basic information
- **[Quick Start](#quick-start)** - Get up and running in 5 minutes

### Core Documentation

- **[API Reference](API.md)** - Complete API endpoint documentation with examples
- **[Models](MODELS.md)** - TTS models, voices, and language support
- **[Configuration](CONFIGURATION.md)** - Environment variables and settings
- **[Architecture](ARCHITECTURE.md)** - System design and components

### Guides

- **[Development Guide](DEVELOPMENT.md)** - Set up local development environment
- **[Deployment Guide](DEPLOYMENT.md)** - Production deployment strategies
- **[Troubleshooting](TROUBLESHOOTING.md)** - Common issues and solutions

### Technical Details

- **[Text Sanitization](TEXT_SANITIZATION.md)** - Thread safety and text processing

### For AI Agents / Contributors

- **[LLM Wiki](LLM_WIKI.md)** - Condensed, agent-optimized architecture reference (keep updated!)
- **[ASR Implementation Plan](ASR_IMPLEMENTATION_PLAN.md)** - Phase-by-phase plan for the in-progress
  ASR (Whisper/Nemotron) feature, with status tracking so work can resume across sessions

---

## Quick Start

### Using Docker (Recommended)

```bash
# Clone the repository
git clone https://github.com/yourusername/SharpAudio.git
cd SharpAudio

# Start the service
docker compose up -d

# Test the API
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-q4","input":"Hello world!","voice":"af_bella"}' \
  --output speech.wav
```

**Access Points:**
- **Web UI:** http://localhost:5768
- **API:** http://localhost:5768/v1/audio/speech
- **Swagger:** http://localhost:5768/swagger

---

## Documentation Overview

### For Users

**Just want to use the API?**
1. Start with [README](../README.md) for quick setup
2. Check [API Reference](API.md) for endpoint details
3. See [Models](MODELS.md) for available voices and languages

**Need to configure something?**
1. See [Configuration](CONFIGURATION.md) for all settings
2. Check [Troubleshooting](TROUBLESHOOTING.md) if something doesn't work

### For Developers

**Want to contribute or extend?**
1. Read [Development Guide](DEVELOPMENT.md) for setup
2. Review [Architecture](ARCHITECTURE.md) to understand the design
3. Check [Text Sanitization](TEXT_SANITIZATION.md) for thread safety details

**Ready to deploy?**
1. Follow [Deployment Guide](DEPLOYMENT.md) for production setup
2. Configure monitoring and health checks
3. Set up auto-scaling if needed

---

## Key Concepts

### Models

SharpAudio supports multiple TTS models:
- **kokoro-q4** - Fast, quantized (production)
- **kokoro-full** - High quality, full precision
- **supertonic-3** - Multilingual, 31 languages

See [Models Documentation](MODELS.md) for details.

### Voices

510+ speaker voices available:
- American (af, am)
- British (bf, bm)
- OpenAI-compatible aliases (alloy, echo, nova, etc.)

See [Voice Catalog](MODELS.md#voice-catalog) for full list.

### Languages

**Kokoro:** 9 languages (English, Spanish, French, Japanese, Chinese, etc.)  
**Supertonic-3:** 31 languages (nearly all major languages)

See [Language Support](MODELS.md#language-support) for details.

---

## Common Tasks

### Synthesize Speech

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "Your text here",
    "voice": "af_bella",
    "speed": 1.0,
    "language": "en-us"
  }' \
  --output output.wav
```

See [API Reference](API.md) for all parameters.

### List Available Models

```bash
curl http://localhost:5768/api/models | jq
```

### Check Service Health

```bash
curl http://localhost:5768/health
```

### View API Documentation

Open http://localhost:5768/swagger in your browser.

---

## Architecture Overview

```
Client → API Endpoints → Service Layer → TTS Engine → ONNX Runtime
                                                    → espeak-ng
```

SharpAudio uses:
- **ASP.NET Core** - REST API
- **ONNX Runtime** - Model inference
- **espeak-ng** - Phonemization (Kokoro)
- **Vue.js** - Web frontend

See [Architecture Documentation](ARCHITECTURE.md) for details.

---

## Configuration Quick Reference

**Environment Variables:**
```bash
HTTP_PORT=5768                        # Server port
MODEL_CACHE_DIR=/cache                # Model storage
MODEL_IDLE_TIMEOUT_SECONDS=60         # Auto-unload idle models
ESPEAK_DATA_DIR=/app/assets/espeak-ng-data  # Phoneme data
```

**Docker Compose:**
```yaml
services:
  sharp-audio:
    image: fhrozen/sharp-audio:latest
    ports:
      - "5768:5768"
    environment:
      MODEL_IDLE_TIMEOUT_SECONDS: 60
    volumes:
      - ./model-cache:/cache
```

See [Configuration Documentation](CONFIGURATION.md) for all options.

---

## API Quick Reference

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/api/models` | GET | List models with details |
| `/v1/models` | GET | OpenAI-compatible model list |
| `/v1/audio/speech` | POST | Synthesize speech (OpenAI-compatible) |
| `/swagger` | GET | Interactive API documentation |

See [API Reference](API.md) for complete documentation.

---

## Troubleshooting Quick Reference

| Issue | Solution |
|-------|----------|
| espeak-ng not found | `sudo apt-get install libespeak-ng1` |
| Port in use | Change `HTTP_PORT` or kill process |
| Out of memory | Enable idle unloading or use kokoro-q4 |
| Model download fails | Check internet, disk space, permissions |
| Poor audio quality | Use kokoro-full, adjust speed, clean text |
| Silent audio | Check logs for synthesis errors |

See [Troubleshooting Guide](TROUBLESHOOTING.md) for detailed solutions.

---

## Development Quick Reference

**Local Development:**
```bash
# Backend
cd src/SharpAudio.Api
dotnet run

# Frontend
cd frontend
pnpm install
pnpm dev
```

**Run Tests:**
```bash
dotnet test
```

**Build Docker Image:**
```bash
docker build -t sharp-audio:local .
```

See [Development Guide](DEVELOPMENT.md) for complete setup.

---

## Deployment Quick Reference

**Docker:**
```bash
docker compose up -d
```

**Kubernetes:**
```bash
kubectl apply -f deployment.yaml
```

**Cloud Platforms:**
- AWS ECS / App Runner
- Azure Container Apps / ACI
- Google Cloud Run

See [Deployment Guide](DEPLOYMENT.md) for production setup.

---

## Model Quick Reference

| Model | Size | Latency | Quality | Languages | Best For |
|-------|------|---------|---------|-----------|----------|
| kokoro-q4 | 100MB | 150ms | Good | 9 | Production, low latency |
| kokoro-full | 350MB | 220ms | Excellent | 9 | High quality |
| supertonic-3 | 200MB | 380ms | Excellent | 31 | Multilingual |

See [Models Documentation](MODELS.md) for details.

---

## Support

### Documentation
- Browse the docs in the `docs/` folder
- Check [Troubleshooting Guide](TROUBLESHOOTING.md)
- Visit [Swagger UI](http://localhost:5768/swagger) when running

### Community
- **Issues:** Report bugs on [GitHub Issues](https://github.com/yourusername/SharpAudio/issues)
- **Discussions:** Ask questions in [GitHub Discussions](https://github.com/yourusername/SharpAudio/discussions)
- **Contributing:** See [Development Guide](DEVELOPMENT.md)

### Resources
- [Main Repository](https://github.com/yourusername/SharpAudio)
- [Kokoro Model](https://huggingface.co/hexgrad/Kokoro-82M)
- [Supertonic Model](https://huggingface.co/Supertone/supertonic-3)
- [ONNX Runtime](https://onnxruntime.ai/)
- [espeak-ng](https://github.com/espeak-ng/espeak-ng)

---

## Contributing

We welcome contributions! To get started:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

See [Development Guide](DEVELOPMENT.md) for details.

---

## License

See [LICENSE](../LICENSE) file for details.

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history (to be created).

---

**Need help?** Check the [Troubleshooting Guide](TROUBLESHOOTING.md) or open an issue on GitHub.

**Ready to deploy?** Follow the [Deployment Guide](DEPLOYMENT.md).

**Want to contribute?** Read the [Development Guide](DEVELOPMENT.md).
