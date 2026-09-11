# API Reference

SharpAudio provides a RESTful API with OpenAI-compatible endpoints for text-to-speech synthesis and
speech-to-text transcription. Which endpoints are available depends on the `SERVER_MODE`
environment variable the instance was started with (`tts` | `asr` | `both`, default `tts`) - check
`GET /api/server-info` to discover this at runtime.

## Base URL

```
http://localhost:5768
```

Change the port via `HOST_PORT` environment variable in docker-compose.yml or `HTTP_PORT` in the container.

## Interactive Documentation

Swagger UI is available at:
```
http://localhost:5768/swagger
```

OpenAPI specification:
```
http://localhost:5768/swagger/v1/swagger.json
```

## Authentication

Currently, no authentication is required. For production deployments, implement authentication at the reverse proxy level (nginx, API Gateway, etc.).

## Endpoints

### Health Check

Check service health status.

**Request:**
```http
GET /health
```

**Response:**
```json
{
  "status": "ok"
}
```

**Status Codes:**
- `200 OK` - Service is healthy

---

### Server Info

Returns which task types this server instance was started with.

**Request:**
```http
GET /api/server-info
```

**Response:**
```json
{
  "ttsEnabled": true,
  "asrEnabled": false
}
```

Driven by the `SERVER_MODE` environment variable (`tts` | `asr` | `both`). The frontend uses this
to decide which UI panel(s)/tabs to show.

**Status Codes:**
- `200 OK` - Success

---

### List Available Models (Detailed)

Get detailed information about all available TTS models.

**Request:**
```http
GET /api/models
```

**Response:**
```json
[
  {
    "name": "kokoro-q4",
    "displayName": "Kokoro Q4",
    "description": "Kokoro ONNX Q4 model with direct OnnxRuntime inference.",
    "supportedLanguages": [
      "en-us", "en-gb", "es", "fr-fr", "hi", 
      "it", "ja-jp", "pt-br", "zh-cn"
    ],
    "speakers": [
      "af", "af_bella", "af_nicole", "af_sarah", 
      "af_sky", "am_adam", "am_michael", ...
    ],
    "speakerMetadata": null
  },
  {
    "name": "supertonic-3",
    "displayName": "Supertonic 3",
    "description": "Supertonic-3 multilingual ONNX TTS model — 31 languages, 10 preset voice styles, flow-matching inference.",
    "supportedLanguages": [
      "en", "es", "fr", "de", "it", "pt", "pl", 
      "tr", "ru", "nl", "cs", "ar", "zh", "ja", 
      "ko", "hu", "hi", "sv", "da", "no", "fi", 
      "el", "ro", "uk", "th", "vi", "id", "he", ...
    ],
    "speakers": [
      "F1", "F2", "F3", "F4", "F5",
      "M1", "M2", "M3", "M4", "M5"
    ],
    "speakerMetadata": [
      {
        "id": "F1",
        "name": "Female 1",
        "description": "Warm, friendly female voice"
      },
      ...
    ]
  }
]
```

**Status Codes:**
- `200 OK` - Success

---

### List Available ASR Models (Detailed)

Get detailed information about all available ASR (speech-to-text) models. Only present when
`SERVER_MODE` enables ASR.

**Request:**
```http
GET /api/asr-models
```

**Response:**
```json
[
  {
    "name": "whisper-base",
    "displayName": "Whisper Base",
    "description": "OpenAI Whisper base multilingual model (GGML), run via whisper.cpp.",
    "supportedLanguages": [],
    "supportsLanguageAutoDetect": true,
    "supportsVad": false
  },
  {
    "name": "nemotron-3.5",
    "displayName": "Nemotron 3.5 ASR",
    "description": "NVIDIA Nemotron 3.5 streaming ASR (cache-aware FastConformer-RNNT, INT4 ONNX).",
    "supportedLanguages": ["en", "es", "fr", "it", "pt", "nl", "de", "tr", "ru", "ar", "hi", "ja", "ko", ".."],
    "supportsLanguageAutoDetect": true,
    "supportsVad": true
  }
]
```

**Status Codes:**
- `200 OK` - Success

---

### List Models (OpenAI Compatible)

Get list of available models in OpenAI-compatible format.

**Request:**
```http
GET /v1/models
```

**Response:**
```json
{
  "object": "list",
  "data": [
    {
      "id": "kokoro-q4",
      "object": "model",
      "created": 1715548800,
      "owned_by": "fastttsr"
    },
    {
      "id": "kokoro-full",
      "object": "model",
      "created": 1715548800,
      "owned_by": "fastttsr"
    },
    {
      "id": "supertonic-3",
      "object": "model",
      "created": 1715548800,
      "owned_by": "fastttsr"
    }
  ]
}
```

**Status Codes:**
- `200 OK` - Success

---

### Synthesize Speech (OpenAI Compatible)

Convert text to speech audio.

**Request:**
```http
POST /v1/audio/speech
Content-Type: application/json

{
  "model": "kokoro-q4",
  "input": "Hello world, this is a test of the text to speech system.",
  "voice": "af_bella",
  "speed": 1.0,
  "language": "en-us",
  "response_format": "wav"
}
```

**Parameters:**

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `model` | string | **Yes** | - | Model ID (`kokoro-q4`, `kokoro-full`, `supertonic-3`) |
| `input` | string | **Yes** | - | Text to synthesize (max ~500 characters recommended) |
| `voice` | string | No | First available | Speaker voice ID or OpenAI alias (`alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer`) |
| `speaker` | string | No | - | Alternative to `voice` parameter (same functionality) |
| `language` | string | No | `en-us` | Language code (see supported languages below) |
| `speed` | number | No | `1.0` | Playback speed (0.5 - 2.0) |
| `response_format` | string | No | `wav` | Audio format (currently only `wav` supported) |

**Supported Languages (Kokoro):**

| Code | Language | Aliases |
|------|----------|---------|
| `en-us` | English (US) | `a`, `en`, `english` |
| `en-gb` | English (GB) | `b` |
| `es` | Spanish | `d`, `spanish` |
| `fr-fr` | French | `e`, `f`, `fr`, `french` |
| `hi` | Hindi | `g`, `hindi` |
| `it` | Italian | `h`, `italian` |
| `ja-jp` | Japanese | `j`, `ja`, `japanese` |
| `pt-br` | Portuguese (BR) | `p`, `pt`, `portuguese` |
| `zh-cn` | Chinese (CN) | `z`, `zh`, `chinese` |

**Supported Languages (Supertonic-3):**

31 languages including: English, Spanish, French, German, Italian, Portuguese, Polish, Turkish, Russian, Dutch, Czech, Arabic, Chinese, Japanese, Korean, Hungarian, Hindi, Swedish, Danish, Norwegian, Finnish, Greek, Romanian, Ukrainian, Thai, Vietnamese, Indonesian, Hebrew, and more.

Use 2-letter ISO codes: `en`, `es`, `fr`, `de`, `it`, `pt`, `ja`, `ko`, `zh`, etc.

**OpenAI Voice Aliases (Kokoro):**

| Alias | Maps To | Description |
|-------|---------|-------------|
| `alloy` | `af_alloy` | Balanced, neutral voice |
| `echo` | `am_echo` | Male voice |
| `fable` | `bf_fable` | British female voice |
| `onyx` | `bm_onyx` | British male voice |
| `nova` | `af_nova` | Bright female voice |
| `shimmer` | `af_shimmer` | Soft female voice |

**Response:**

Returns audio file as `audio/wav` stream.

**Response Headers:**
```
Content-Type: audio/wav
Content-Length: <size>
```

**WAV Format:**
- **Sample Rate:** 24000 Hz (Kokoro) or 22050 Hz (Supertonic-3)
- **Bit Depth:** 16-bit
- **Channels:** Mono
- **Format:** PCM

**Status Codes:**
- `200 OK` - Success (returns WAV file)
- `400 Bad Request` - Invalid parameters
- `404 Not Found` - Model not found
- `500 Internal Server Error` - Synthesis failure (returns silent WAV)

**Error Response Format:**
```json
{
  "error": {
    "code": "invalid_request",
    "message": "Unsupported language 'xyz'. Supported languages: en-us, en-gb, es, ..."
  }
}
```

---

### Transcribe Audio (OpenAI Compatible)

Convert speech audio to text. Only present when `SERVER_MODE` enables ASR.

**Request:**
```http
POST /v1/audio/transcriptions
Content-Type: multipart/form-data

file=@sample.wav
model=whisper-base
language=en                    (optional)
response_format=verbose_json   (optional)
min_segment_duration=0.5       (optional)
```

**Form Fields:**

| Field | Required | Default | Description |
|-------|----------|---------|--------------|
| `file` | **Yes** | - | Audio file to transcribe. Any format `ffmpeg` can decode (WAV, FLAC, MP3, OGG, WEBM, M4A, etc.) - normalized server-side to PCM16 mono WAV before either engine sees it |
| `model` | **Yes** | - | Model ID (`whisper-base`, `nemotron-3.5`) |
| `language` | No | Auto-detect | ISO language code hint (e.g. `en`, `ja`), or `auto` to explicitly request auto-detection; both models support auto-detection if omitted |
| `response_format` | No | `json` | `json` returns `{ text }` only. `verbose_json` (OpenAI-compatible name) additionally returns a `segments` array of timestamped segments - see below |
| `min_segment_duration` | No | `0.5` | Seconds. Only used with `response_format=verbose_json`. Segments shorter than this are merged into a neighboring segment (never dropped) |
| `use_vad` | No | `false` | `true`/`1` to enable voice-activity-detection gating (skips inference on silent audio chunks) - only affects `nemotron-3.5` (`supportsVad: true`); ignored by models that don't support it. With `response_format=verbose_json`, Nemotron's segments reflect the detected speech regions when this is enabled, or one segment spanning the whole clip when it isn't |

**Response (`response_format=json`, default):**
```json
{
  "text": "Hello, this is a test of the transcription system."
}
```

**Response (`response_format=verbose_json`):**
```json
{
  "text": "Hello, this is a test of the transcription system.",
  "language": "en",
  "duration": 3.1,
  "segments": [
    { "id": 1, "start": 0.0, "end": 1.6, "text": "Hello, this is a test" },
    { "id": 2, "start": 1.6, "end": 3.1, "text": "of the transcription system." }
  ]
}
```
Abbreviated compared to OpenAI's own `verbose_json` shape (only `id`/`start`/`end`/`text` per
segment - no `tokens`/`avg_logprob`/etc.). **Worker-mode note**: segment timestamps are currently
only populated when the server runs ASR in-process (`AsrWorkerOptions__Enabled=false`) - in worker
mode (the default), `segments` is returned as an empty array since the worker's gRPC protocol
doesn't carry segment data yet.

**Response Headers:**
```
X-Processing-Time: 0.842      # seconds spent transcribing
X-Audio-Duration: 3.10        # duration of the input audio, seconds
X-RTF: 0.272                  # real-time factor (processing time / audio duration)
X-Character-Count: 52         # length of the returned text
```

**Status Codes:**
- `200 OK` - Success
- `400 Bad Request` - Missing/invalid form data (e.g. no file provided, not `multipart/form-data`)
- `404 Not Found` - Model not found

**Error Response Format:**
```json
{
  "error": {
    "code": "model_not_found",
    "message": "The requested model was not found."
  }
}
```

---

## cURL Examples

### Basic Synthesis (Kokoro)

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "Hello world! This is a test of the SharpAudio text-to-speech system.",
    "voice": "af_bella"
  }' \
  --output output.wav
```

### With Speed Control

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "This is a faster speech sample.",
    "voice": "am_michael",
    "speed": 1.5
  }' \
  --output fast.wav
```

### Japanese Synthesis

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "こんにちは、世界！",
    "voice": "af_bella",
    "language": "ja-jp"
  }' \
  --output japanese.wav
```

### Chinese Synthesis

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "你好世界！这是一个测试。",
    "voice": "af_sky",
    "language": "zh-cn"
  }' \
  --output chinese.wav
```

### Using OpenAI Aliases

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "kokoro-q4",
    "input": "Using OpenAI-compatible voice aliases.",
    "voice": "alloy"
  }' \
  --output alloy.wav
```

### Supertonic-3 Model

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "supertonic-3",
    "input": "This is the Supertonic three model with higher quality.",
    "voice": "F1",
    "language": "en"
  }' \
  --output supertonic.wav
```

### Transcribe Audio (Whisper)

```bash
curl -X POST http://localhost:5768/v1/audio/transcriptions \
  -F file=@sample.wav \
  -F model=whisper-base
```

### Transcribe Audio (Nemotron, with a language hint)

```bash
curl -X POST http://localhost:5768/v1/audio/transcriptions \
  -F file=@sample.wav \
  -F model=nemotron-3.5 \
  -F language=en \
  -i
```

---

## Python Examples

### Using requests

```python
import requests

url = "http://localhost:5768/v1/audio/speech"
headers = {"Content-Type": "application/json"}
payload = {
    "model": "kokoro-q4",
    "input": "Hello from Python!",
    "voice": "af_bella",
    "speed": 1.0
}

response = requests.post(url, json=payload, headers=headers)

if response.status_code == 200:
    with open("output.wav", "wb") as f:
        f.write(response.content)
    print("Audio saved to output.wav")
else:
    print(f"Error: {response.status_code}")
    print(response.json())
```

### Transcribing Audio

```python
import requests

url = "http://localhost:5768/v1/audio/transcriptions"

with open("sample.wav", "rb") as f:
    response = requests.post(
        url,
        files={"file": f},
        data={"model": "whisper-base", "language": "en"}
    )

if response.status_code == 200:
    print(response.json()["text"])
    print("RTF:", response.headers.get("X-RTF"))
else:
    print(f"Error: {response.status_code}")
    print(response.json())
```

### Using OpenAI SDK

```python
from openai import OpenAI

# Point to SharpAudio instead of OpenAI
client = OpenAI(
    api_key="not-needed",  # No API key required
    base_url="http://localhost:5768/v1"
)

response = client.audio.speech.create(
    model="kokoro-q4",
    voice="alloy",
    input="Hello from OpenAI SDK!"
)

response.stream_to_file("output.wav")

# ASR (Whisper-compatible) - only when SERVER_MODE enables ASR
with open("sample.wav", "rb") as audio_file:
    transcript = client.audio.transcriptions.create(
        model="whisper-base",
        file=audio_file
    )
print(transcript.text)
```

---

## JavaScript/Node.js Examples

### Using fetch

```javascript
const response = await fetch('http://localhost:5768/v1/audio/speech', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    model: 'kokoro-q4',
    input: 'Hello from JavaScript!',
    voice: 'af_bella'
  })
});

if (response.ok) {
  const buffer = await response.arrayBuffer();
  const fs = require('fs');
  fs.writeFileSync('output.wav', Buffer.from(buffer));
  console.log('Audio saved to output.wav');
} else {
  const error = await response.json();
  console.error('Error:', error);
}
```

### Using axios

```javascript
const axios = require('axios');
const fs = require('fs');

const response = await axios.post('http://localhost:5768/v1/audio/speech', {
  model: 'kokoro-q4',
  input: 'Hello from axios!',
  voice: 'af_bella'
}, {
  responseType: 'arraybuffer'
});

fs.writeFileSync('output.wav', response.data);
console.log('Audio saved to output.wav');
```

---

## Rate Limiting

Currently, no rate limiting is implemented at the application level. For production:

1. **Reverse Proxy:** Implement rate limiting in nginx/Apache
2. **API Gateway:** Use cloud provider's API Gateway with rate limits
3. **Application Level:** Add rate limiting middleware (future enhancement)

**Recommended Limits:**
- 100 requests/minute per IP for free tier
- 1000 requests/minute for authenticated users

---

## Error Codes

| Code | HTTP Status | Description | Solution |
|------|-------------|-------------|----------|
| `model_not_found` | 404 | Model ID not found | Check available models with `GET /api/models` |
| `invalid_request` | 400 | Invalid request parameters | Check parameter format and values |
| `unsupported_language` | 400 | Language not supported by model | Use supported language from model info |
| `unsupported_speaker` | 400 | Speaker/voice not found | Use valid speaker from model info |
| `synthesis_failure` | 500 | TTS synthesis failed | Check logs; service returns silent WAV |

---

## Response Time

Typical response times (including audio generation):

| Model | Input Length | Response Time | Notes |
|-------|--------------|---------------|-------|
| kokoro-q4 | 50 chars | 60-100ms | Fastest |
| kokoro-q4 | 200 chars | 150-250ms | Typical sentence |
| kokoro-full | 50 chars | 80-120ms | Higher quality |
| kokoro-full | 200 chars | 200-350ms | Best quality |
| supertonic-3 | 50 chars | 100-200ms | Multi-model |
| supertonic-3 | 200 chars | 300-500ms | Highest quality |

**Factors Affecting Performance:**
- Input text length
- Model complexity
- Server CPU/GPU
- Concurrent requests
- First request (model loading)

---

## Best Practices

### 1. Input Text

- **Length:** Keep under 500 characters for optimal performance
- **Format:** Plain text works best
- **Special Characters:** Automatically sanitized
- **Markdown:** Automatically cleaned (bullets, formatting, etc.)

### 2. Language Selection

- Always specify language for non-English text
- Use correct language code for pronunciation
- Mixed-language input may produce unexpected results

### 3. Speed Parameter

- Range: 0.5 (slow) to 2.0 (fast)
- Default: 1.0 (normal)
- Values outside range will be clamped

### 4. Error Handling

- Always check HTTP status code
- Parse error response for details
- Implement retry logic with exponential backoff
- On 500 errors, service returns valid (silent) WAV

### 5. Performance

- Reuse connections (HTTP keep-alive)
- Batch requests when possible
- Cache audio for repeated phrases
- Use appropriate model for use case (q4 vs full)

---

## Compatibility

### OpenAI API Compatibility

SharpAudio implements a subset of OpenAI's `/v1/audio/speech` endpoint:

**Compatible:**
- ✅ `model` parameter
- ✅ `input` parameter
- ✅ `voice` parameter
- ✅ `speed` parameter
- ✅ Response format (audio/wav)

**Extensions:**
- ➕ `language` parameter (explicitly set language)
- ➕ `speaker` parameter (alternative to `voice`)
- ➕ Extended speaker catalog (510+ voices)
- ➕ Multiple TTS models

**Not Implemented:**
- ❌ `response_format` other than `wav` (mp3, opus, aac, flac)
- ❌ Audio streaming (planned)

### Migration from OpenAI

To migrate from OpenAI TTS:

1. Change `base_url` to SharpAudio endpoint
2. Remove or ignore `api_key` (not required)
3. Convert voice names if using custom voices
4. Add explicit `language` parameter for non-English

**Example:**
```python
# Before (OpenAI)
client = OpenAI(api_key="sk-...")

# After (SharpAudio)
client = OpenAI(
    api_key="not-needed",
    base_url="http://localhost:5768/v1"
)
```

---

## Future Enhancements

- [ ] Audio streaming (chunked transfer)
- [ ] Additional audio formats (mp3, opus, flac)
- [ ] Batch synthesis endpoint
- [ ] SSML support
- [ ] Voice cloning
- [ ] Real-time WebSocket API
- [ ] Audio post-processing (effects, normalization)
- [ ] Rate limiting and quota management
- [ ] API key authentication
