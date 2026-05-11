# FastTTSR

Web & REST API for TTS (OpenAI-compatible endpoint), with a Vue frontend and Docker packaging.

## Implemented scope

- C# REST API compatible with `POST /v1/audio/speech`
- Supported models in API contract:
  - `kokoro-tts`
  - `supertonic-3`
- Model download from Hugging Face on startup into cache directory (`MODEL_CACHE_DIR`)
- Vue.js frontend (pnpm) with model/language/speaker selection
- Dockerfile and docker-compose for a single service deployment

## Backend API

### Endpoints

- `GET /health`
- `GET /api/models`
- `POST /v1/audio/speech`

Set `SHERPA_ONNX_TTS_CLI` to a sherpa-onnx TTS CLI executable to run model inference through sherpa-onnx.  
If not set, the service returns generated WAV output for API-flow testing.

Example request:

```bash
curl -X POST http://localhost:8080/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{"model":"kokoro-tts","input":"Hello world"}' \
  --output speech.wav
```

## Frontend

```bash
cd frontend
corepack enable pnpm
pnpm install --ignore-scripts
pnpm dev
```

## Docker

Build and run with compose:

```bash
docker compose up --build
```

The model cache is stored in the `model-cache` volume and mapped to `/cache` in the container.

## Tests

```bash
dotnet test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj
```
