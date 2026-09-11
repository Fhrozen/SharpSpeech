# Configuration

SharpAudio can be configured through environment variables, configuration files, and docker-compose settings.

## Environment Variables

### Server Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `HTTP_PORT` | `5768` | HTTP port to listen on |
| `HTTPS_PORT` | - | HTTPS port (optional, requires a certificate - see [HTTPS / TLS](#https--tls) below) |
| `ASPNETCORE_URLS` | `http://+:8080` | ASP.NET Core listening URLs |
| `HOST_PORT` | `5768` | Docker host port mapping (docker-compose only) |
| `HOST_HTTPS_PORT` | `5769` | Docker host port mapping for HTTPS (docker-compose only) |
| `SERVER_MODE` | `tts` | Which task types to serve: `tts`, `asr`, or `both`. Gates TTS/ASR service registration and endpoint mapping - see [Architecture](ARCHITECTURE.md#asr-architecture) |

**Example:**
```bash
HTTP_PORT=8080 dotnet run

# Serve both TTS and ASR from one instance
SERVER_MODE=both dotnet run
```

### HTTPS / TLS

Browsers only expose `navigator.mediaDevices` (mic/tab capture, used by the ASR live-transcription
panel) on a secure context - HTTPS, or exactly `http://localhost`. To serve HTTPS on a LAN
hostname/IP, SharpAudio relies on Kestrel's built-in certificate config - no application code is
involved in loading the certificate:

1. Generate a certificate: `./generate-cert.sh <your-lan-hostname-or-ip>` (prints a generated
   password and writes `./certs/fastttsr.pfx`). It picks the best available method automatically:
   - Locally-installed `mkcert`, if present - a locally-trusted CA, no browser warnings at all.
   - Otherwise a dockerized `mkcert` (`./docker/mkcert`, no local install/sudo needed) - writes the
     CA under `./certs/mkcert-ca/`. **Import `./certs/mkcert-ca/rootCA.pem` into the trust store of
     the machine that will run the BROWSER** (mkcert can't reach a browser trust store from inside
     a container) - Windows: double-click it → "Install Certificate" → Local Machine → "Trusted
     Root Certification Authorities"; then fully restart the browser.
   - Otherwise a plain openssl self-signed cert - browsers show a one-time warning for the page,
     but may still silently reject the live-transcription WebSocket even after you accept it (a
     known Chromium limitation: a manually-bypassed cert warning doesn't reliably extend to
     `new WebSocket()`'s own TLS handshake) - prefer one of the mkcert paths above if streaming
     ASR over HTTPS doesn't connect.
2. Set in `.env`: `HTTPS_PORT=5769`, `HOST_HTTPS_PORT=5769`, `CERT_PASSWORD=<printed password>`.
3. `docker compose up` - the compose file already maps the HTTPS port, mounts `./certs:/certs:ro`,
   and sets `Kestrel__Certificates__Default__Path`/`Password` from `CERT_PASSWORD`.
4. Browse to `https://<hostname>:5769`.

Once `HTTPS_PORT` is set, plain HTTP requests are automatically redirected to HTTPS
(`UseHttpsRedirection`). Leaving `HTTPS_PORT` unset preserves today's HTTP-only behavior.

This is scoped to LAN/internal self-signed use. For a publicly reachable domain with a real
certificate (Let's Encrypt/ACME), prefer a reverse proxy in front of SharpAudio - see
[Reverse Proxy Configuration](#reverse-proxy-configuration) below.

### Model Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `MODEL_CACHE_DIR` | `/cache` | Directory for downloaded models and assets |
| `MODEL_CONFIG_PATH` | `config.json` | Path to model configuration file |
| `ESPEAK_DATA_DIR` | `/app/assets/espeak-ng-data` | espeak-ng phoneme data directory |

**Example:**
```bash
MODEL_CACHE_DIR=/var/cache/ttsr \
MODEL_CONFIG_PATH=/etc/ttsr/models.json \
dotnet SharpAudio.Api.dll
```

### Model Memory Management

| Variable | Default | Description |
|----------|---------|-------------|
| `MODEL_IDLE_TIMEOUT_SECONDS` | `60` | Seconds before unloading idle models (0 = never unload) |

**Behavior:**
- Models are loaded on first use
- After `MODEL_IDLE_TIMEOUT_SECONDS` of inactivity, models are unloaded from memory
- Set to `0` to keep models in memory indefinitely
- Recommended: `60` for production, `0` for development

**Memory Impact:**
```
Idle Timeout    Memory Usage      First Request Latency
    0           High (all models) Low (instant)
    60          Medium            Medium (50-200ms reload)
    300         Low               Medium (50-200ms reload)
```

### Logging Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `Logging__LogLevel__Default` | `Information` | Default log level |
| `Logging__LogLevel__Microsoft.AspNetCore` | `Warning` | ASP.NET Core log level |

**Log Levels:**
- `Trace` - Very detailed (including sensitive data)
- `Debug` - Debugging information
- `Information` - General informational messages
- `Warning` - Warnings and potential issues
- `Error` - Error events
- `Critical` - Critical failures

**Example:**
```bash
Logging__LogLevel__Default=Debug \
Logging__LogLevel__Microsoft.AspNetCore=Information \
dotnet run
```

### Worker Process Configuration

TTS and ASR each run in their own out-of-process worker (`WorkerOptions`/`AsrWorkerOptions`
sections, bound from environment via the standard ASP.NET Core `Section__Property` convention).

| Variable | Default | Description |
|----------|---------|-------------|
| `WorkerOptions__Enabled` | `true` | Use the out-of-process TTS worker (`false` = in-process synthesizers) |
| `WorkerOptions__ExecutablePath` | `./SharpAudio.Worker` | Path to the TTS worker executable |
| `WorkerOptions__IdleTimeoutSeconds` | `60` | Seconds of inactivity before the TTS worker self-terminates |
| `WorkerOptions__PortRangeStart` | `50051` | Starting port for the TTS worker's gRPC server |
| `AsrWorkerOptions__Enabled` | `true` | Use the out-of-process ASR worker (`false` = in-process transcribers) |
| `AsrWorkerOptions__ExecutablePath` | `./worker-asr/SharpAudio.Worker.Asr` | Path to the ASR worker executable |
| `AsrWorkerOptions__IdleTimeoutSeconds` | `60` | Seconds of inactivity before the ASR worker self-terminates |
| `AsrWorkerOptions__PortRangeStart` | `50151` | Starting port for the ASR worker's gRPC server (distinct range from TTS's `50051+`) |

**Example:**
```bash
AsrWorkerOptions__IdleTimeoutSeconds=120 \
AsrWorkerOptions__PortRangeStart=50151 \
dotnet run
```

---

## Configuration Files

### appsettings.json

Located at `src/SharpAudio.Api/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ModelIdleMonitor": {
    "IdleTimeoutSeconds": 60,
    "CheckIntervalSeconds": 10
  },
  "WorkerOptions": {
    "Enabled": true,
    "ExecutablePath": "./SharpAudio.Worker",
    "IdleTimeoutSeconds": 60,
    "PortRangeStart": 50051,
    "MaxPortAttempts": 100,
    "StartupTimeoutSeconds": 30
  }
}
```

#### Worker Process Configuration

**WorkerOptions** controls the worker process mode for model isolation. When enabled, models run in separate processes instead of in the main API process.

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable worker process mode. Set to `false` for in-process mode |
| `ExecutablePath` | `./SharpAudio.Worker` | Path to worker executable |
| `IdleTimeoutSeconds` | `60` | Seconds before worker self-terminates (0 = never) |
| `PortRangeStart` | `50051` | Starting port for worker gRPC servers |
| `MaxPortAttempts` | `100` | Maximum ports to try before failing |
| `StartupTimeoutSeconds` | `30` | Max time to wait for worker startup |

**Worker Mode Benefits:**
- ✅ True OS-level memory isolation per model
- ✅ Guaranteed memory reclamation when worker exits
- ✅ Fault isolation - worker crashes don't affect API
- ✅ Automatic respawn on failure
- ✅ Reduced baseline memory (~50MB vs ~300MB+)

**Environment Variable Overrides:**
```bash
# Disable worker mode
WorkerOptions__Enabled=false

# Change idle timeout
WorkerOptions__IdleTimeoutSeconds=120

# Docker deployment path
WorkerOptions__ExecutablePath=/app/worker/SharpAudio.Worker
```

**Mode Comparison:**

| Aspect | Worker Mode (Enabled=true) | In-Process Mode (Enabled=false) |
|--------|----------------------------|----------------------------------|
| Memory Isolation | OS-level (separate process) | Managed (.NET GC) |
| Memory Cleanup | Guaranteed on exit | Best-effort via Dispose() |
| Baseline Memory | ~50MB (API only) | ~50MB + all models |
| Idle Memory | ~50MB (workers terminated) | ~50-800MB (models loaded) |
| First Request | +200-500ms (spawn + load) | +50-200ms (load only) |
| Subsequent | +1-3ms (gRPC overhead) | 0ms (direct call) |
| Fault Isolation | High (process boundary) | Low (shared memory) |
| Complexity | Higher (IPC, process mgmt) | Lower (direct calls) |

**When to Use Worker Mode:**
- ✅ Production deployments with multiple models
- ✅ Memory-constrained environments
- ✅ Long-running services with bursty traffic
- ✅ When model unloading is critical

**When to Use In-Process Mode:**
- ✅ Development and testing
- ✅ Single-model deployments
- ✅ High-throughput, low-latency requirements
- ✅ Simplified debugging

#### ModelIdleMonitor Section

| Setting | Default | Description |
|---------|---------|-------------|
| `IdleTimeoutSeconds` | `60` | Seconds before unloading idle models |
| `CheckIntervalSeconds` | `10` | How often to check for idle models |

**Example:**
```json
{
  "ModelIdleMonitor": {
    "IdleTimeoutSeconds": 300,
    "CheckIntervalSeconds": 30
  }
}
```

### appsettings.Development.json

Override settings for development environment:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "ModelIdleMonitor": {
    "IdleTimeoutSeconds": 0
  }
}
```

**Note:** This file is loaded when `ASPNETCORE_ENVIRONMENT=Development`

---

### config.json (Model Configuration)

Located at `src/SharpAudio.Api/config.json` or path specified by `MODEL_CONFIG_PATH`.

Defines available TTS models (`models` array) and ASR models (`asrModels` array), their assets,
and metadata. Both are loaded from the same file.

**Structure:**
```json
{
  "models": [
    {
      "name": "model-id",
      "displayName": "Display Name",
      "description": "Model description",
      "engine": "kokoro|supertonic-3",
      "modelPath": "relative/path/to/model",
      "tokensPath": "/absolute/path/to/tokens.txt",
      "voicesPath": "relative/voices/path",
      "voicesBaseUrl": "https://huggingface.co/.../resolve/main/",
      "voiceFileExtension": ".bin|.json",
      "assets": [
        {
          "relativePath": "onnx/model.onnx",
          "url": "https://huggingface.co/.../model.onnx"
        }
      ],
      "supportedLanguages": [],
      "speakers": []
    }
  ]
}
```

#### Model Definition Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | **Yes** | Unique model ID |
| `displayName` | string | No | Human-readable name |
| `description` | string | No | Model description |
| `engine` | string | **Yes** | Engine type: `kokoro` or `supertonic-3` |
| `modelPath` | string | **Yes** | Relative path to model file(s) |
| `tokensPath` | string | Kokoro only | Path to tokens.txt |
| `voicesPath` | string | **Yes** | Relative path to voice files |
| `voicesBaseUrl` | string | **Yes** | Base URL for downloading voice files |
| `voiceFileExtension` | string | **Yes** | Voice file extension (`.bin` or `.json`) |
| `assets` | array | **Yes** | List of downloadable assets |
| `supportedLanguages` | array | Auto | Supported language codes (auto-populated) |
| `speakers` | array | Auto | Available speakers (auto-populated) |

#### Example: Kokoro Model

```json
{
  "name": "kokoro-q4",
  "displayName": "Kokoro Q4",
  "description": "Quantized Kokoro 82M ONNX model",
  "engine": "kokoro",
  "modelPath": "onnx/model_q4.onnx",
  "tokensPath": "/app/assets/tokens.txt",
  "voicesPath": "voices",
  "voicesBaseUrl": "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices",
  "voiceFileExtension": ".bin",
  "assets": [
    {
      "relativePath": "onnx/model_q4.onnx",
      "url": "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model_q4.onnx"
    }
  ]
}
```

#### Example: Supertonic Model

```json
{
  "name": "supertonic-3",
  "displayName": "Supertonic 3",
  "description": "Multilingual flow-matching TTS model",
  "engine": "supertonic-3",
  "modelPath": "onnx",
  "voicesPath": "voice_styles",
  "voicesBaseUrl": "https://huggingface.co/Supertone/supertonic-3/resolve/main/voice_styles",
  "voiceFileExtension": ".json",
  "assets": [
    {
      "relativePath": "onnx/text_encoder.onnx",
      "url": "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/text_encoder.onnx"
    },
    {
      "relativePath": "onnx/duration_predictor.onnx",
      "url": "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/duration_predictor.onnx"
    },
    {
      "relativePath": "onnx/vector_estimator.onnx",
      "url": "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/vector_estimator.onnx"
    },
    {
      "relativePath": "onnx/vocoder.onnx",
      "url": "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/vocoder.onnx"
    },
    {
      "relativePath": "onnx/tts.json",
      "url": "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/tts.json"
    },
    {
      "relativePath": "onnx/unicode_indexer.json",
      "url": "https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/unicode_indexer.json"
    }
  ]
}
```

#### ASR Model Definition Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | **Yes** | Unique model ID |
| `displayName` | string | No | Human-readable name |
| `description` | string | No | Model description |
| `engine` | string | **Yes** | Engine type: `whisper` or `nemotron-3.5` |
| `modelPath` | string | Whisper only | Relative path to the `.bin` GGML model file |
| `assets` | array | **Yes** | List of downloadable assets |
| `supportedLanguages` | array | No | Supported language codes (empty = relies on auto-detect) |
| `supportsLanguageAutoDetect` | bool | No | Whether the model can auto-detect the spoken language |
| `supportsVad` | bool | No | Whether the model supports voice-activity-detection gating (`nemotron-3.5` only; requires the `silero_vad.onnx` asset) |

#### Example: Whisper Model

```json
{
  "name": "whisper-base",
  "displayName": "Whisper Base",
  "description": "OpenAI Whisper base multilingual model (GGML), run via whisper.cpp.",
  "engine": "whisper",
  "modelPath": "ggml-base.bin",
  "assets": [
    {
      "relativePath": "ggml-base.bin",
      "url": "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
    }
  ],
  "supportedLanguages": [],
  "supportsLanguageAutoDetect": true,
  "supportsVad": false
}
```

#### Example: Nemotron Model

```json
{
  "name": "nemotron-3.5",
  "displayName": "Nemotron 3.5 ASR",
  "description": "NVIDIA Nemotron 3.5 streaming ASR (cache-aware FastConformer-RNNT, INT4 ONNX).",
  "engine": "nemotron-3.5",
  "modelPath": "",
  "assets": [
    { "relativePath": "encoder.onnx", "url": "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/encoder.onnx" },
    { "relativePath": "encoder.onnx.data", "url": "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/encoder.onnx.data" },
    { "relativePath": "decoder.onnx", "url": "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/decoder.onnx" },
    { "relativePath": "joint.onnx", "url": "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/joint.onnx" },
    { "relativePath": "vocab.txt", "url": "https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4/resolve/main/vocab.txt" }
  ],
  "supportedLanguages": ["en", "es", "fr", "ja", "..."],
  "supportsLanguageAutoDetect": true,
  "supportsVad": true
}
```

> The full asset list (encoder/decoder/joint each ship an accompanying `.onnx.data` file, plus
> `audio_processor_config.json` and `tokenizer.json`) is in `src/SharpAudio.Api/config.json`.

---

## Docker Compose Configuration

### docker-compose.yml

```yaml
services:
  fastttsr:
    image: fhrozen/fast-ttsr:latest
    ports:
      - "${HOST_PORT:-5768}:${HTTP_PORT:-5768}"
      - "${HOST_HTTPS_PORT:-5769}:${HTTPS_PORT:-5769}"
    environment:
      HTTP_PORT: ${HTTP_PORT:-5768}
      HTTPS_PORT: ${HTTPS_PORT:-}
      SERVER_MODE: ${SERVER_MODE:-both}
      MODEL_CACHE_DIR: /cache
      ESPEAK_DATA_DIR: /app/assets/espeak-ng-data
      MODEL_IDLE_TIMEOUT_SECONDS: ${MODEL_IDLE_TIMEOUT_SECONDS:-60}
      AsrWorkerOptions__Enabled: ${AsrWorkerOptions__Enabled:-true}
      AsrWorkerOptions__PortRangeStart: ${AsrWorkerOptions__PortRangeStart:-50151}
      AsrWorkerOptions__IdleTimeoutSeconds: ${AsrWorkerOptions__IdleTimeoutSeconds:-60}
      # Only used once HTTPS_PORT is set - see "HTTPS / TLS" above.
      Kestrel__Certificates__Default__Path: /certs/fastttsr.pfx
      Kestrel__Certificates__Default__Password: ${CERT_PASSWORD:-}
    volumes:
      - ./model-cache:/cache
      - ./assets:/app/assets:ro
      - ./certs:/certs:ro
```

Use `docker build --target runtime-tts`/`runtime-asr`/`runtime-all` (see [Dockerfile](../Dockerfile))
to build a smaller single-purpose image instead of the default multi-purpose one; pair with
`SERVER_MODE: tts`/`asr` accordingly.

### Environment File (.env)

Create a `.env` file in the project root:

```bash
# Server Configuration
HOST_PORT=5768
HTTP_PORT=5768
HTTPS_PORT=
HOST_HTTPS_PORT=5769
CERT_PASSWORD=
SERVER_MODE=both

# Model Configuration
MODEL_IDLE_TIMEOUT_SECONDS=60
AsrWorkerOptions__IdleTimeoutSeconds=60

# Logging
Logging__LogLevel__Default=Information
```

**Usage:**
```bash
docker compose up -d
```

The `.env` file is automatically loaded by docker-compose.

---

## Volume Mounts

### Model Cache Volume

**Path:** `./model-cache:/cache`

**Purpose:**
- Store downloaded ONNX models
- Cache speaker voice files
- Persist across container restarts

**Structure:**
```
model-cache/
├── kokoro-q4/
│   ├── onnx/
│   │   └── model_q4.onnx
│   └── voices/
│       ├── af.bin
│       ├── af_bella.bin
│       └── ...
├── kokoro-full/
│   ├── onnx/
│   │   └── model.onnx
│   └── voices/
│       └── ...
└── supertonic-3/
    ├── onnx/
    │   ├── text_encoder.onnx
    │   ├── duration_predictor.onnx
    │   ├── vector_estimator.onnx
    │   ├── vocoder.onnx
    │   ├── tts.json
    │   └── unicode_indexer.json
    └── voice_styles/
        ├── F1.json
        ├── F2.json
        └── ...
```

**Permissions:**
- Container runs as non-root by default
- Ensure cache directory is writable: `chmod 777 model-cache`

### Assets Volume

**Path:** `./assets:/app/assets:ro`

**Purpose:**
- Provide static assets (tokens.txt, espeak-ng-data)
- Mounted read-only for security

**Required Files:**
```
assets/
├── tokens.txt           # Kokoro phoneme-to-token mapping
└── espeak-ng-data/      # espeak-ng phoneme database
    ├── en_dict
    ├── ja_dict
    ├── zh_dict
    └── ...
```

**Note:** espeak-ng-data is installed automatically in the Docker image. This mount is optional unless using custom phoneme data.

---

## Production Configuration

### Recommended Settings

```yaml
services:
  fastttsr:
    image: fhrozen/fast-ttsr:latest
    restart: unless-stopped
    ports:
      - "127.0.0.1:5768:5768"  # Only bind to localhost
    environment:
      HTTP_PORT: 5768
      MODEL_CACHE_DIR: /cache
      ESPEAK_DATA_DIR: /app/assets/espeak-ng-data
      MODEL_IDLE_TIMEOUT_SECONDS: 60
      Logging__LogLevel__Default: Warning
      Logging__LogLevel__Microsoft.AspNetCore: Warning
    volumes:
      - ./model-cache:/cache
      - ./assets:/app/assets:ro
    deploy:
      resources:
        limits:
          memory: 4G
          cpus: '2.0'
        reservations:
          memory: 1G
          cpus: '1.0'
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5768/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 60s
```

### Resource Limits

| Resource | Minimum | Recommended | Maximum |
|----------|---------|-------------|---------|
| Memory | 1 GB | 4 GB | 8 GB |
| CPU | 1 core | 2 cores | 4 cores |
| Storage | 5 GB | 10 GB | 20 GB |

### Security Hardening

1. **Bind to localhost only**
   ```yaml
   ports:
     - "127.0.0.1:5768:5768"
   ```

2. **Use reverse proxy** (nginx, traefik, caddy)
   - SSL/TLS termination
   - Rate limiting
   - Authentication
   - Request size limits

3. **Read-only root filesystem**
   ```yaml
   read_only: true
   tmpfs:
     - /tmp
   ```

4. **Drop capabilities**
   ```yaml
   cap_drop:
     - ALL
   ```

5. **Set resource limits** (see above)

---

## Reverse Proxy Configuration

For LAN/internal use, prefer the Kestrel-native HTTPS flow described in [HTTPS / TLS](#https--tls)
above - it needs no extra container. The nginx/Caddy examples below are an alternative for a
publicly reachable deployment (real Let's Encrypt/ACME certs, rate limiting, or terminating TLS
for multiple services at once); they are illustrative snippets, not files shipped in this repo.

### nginx

```nginx
upstream fastttsr {
    server 127.0.0.1:5768;
}

server {
    listen 80;
    server_name tts.example.com;

    # Redirect to HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name tts.example.com;

    ssl_certificate /etc/ssl/certs/tts.example.com.crt;
    ssl_certificate_key /etc/ssl/private/tts.example.com.key;

    # Rate limiting
    limit_req_zone $binary_remote_addr zone=tts:10m rate=10r/s;
    limit_req zone=tts burst=20 nodelay;

    # Timeouts
    proxy_connect_timeout 60s;
    proxy_send_timeout 60s;
    proxy_read_timeout 60s;

    # Request size limit
    client_max_body_size 10M;

    location / {
        proxy_pass http://fastttsr;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # CORS headers (if needed)
        add_header Access-Control-Allow-Origin * always;
        add_header Access-Control-Allow-Methods "GET, POST, OPTIONS" always;
        add_header Access-Control-Allow-Headers "Content-Type" always;
    }

    # Health check endpoint
    location /health {
        proxy_pass http://fastttsr/health;
        access_log off;
    }
}
```

### Caddy

```caddy
tts.example.com {
    reverse_proxy localhost:5768

    # Rate limiting
    rate_limit {
        zone tts 10r/s
    }

    # Request size limit
    request_body {
        max_size 10MB
    }

    # Timeouts
    timeouts {
        read_body 60s
        read_header 60s
        write 60s
        idle 60s
    }

    # CORS (if needed)
    header Access-Control-Allow-Origin *
    header Access-Control-Allow-Methods "GET, POST, OPTIONS"
    header Access-Control-Allow-Headers "Content-Type"
}
```

---

## Cloud Configuration Examples

### AWS ECS Task Definition

```json
{
  "family": "fastttsr",
  "containerDefinitions": [
    {
      "name": "fastttsr",
      "image": "fhrozen/fast-ttsr:latest",
      "memory": 4096,
      "cpu": 2048,
      "essential": true,
      "environment": [
        {
          "name": "HTTP_PORT",
          "value": "5768"
        },
        {
          "name": "MODEL_IDLE_TIMEOUT_SECONDS",
          "value": "60"
        }
      ],
      "portMappings": [
        {
          "containerPort": 5768,
          "protocol": "tcp"
        }
      ],
      "mountPoints": [
        {
          "sourceVolume": "model-cache",
          "containerPath": "/cache"
        }
      ],
      "healthCheck": {
        "command": ["CMD-SHELL", "curl -f http://localhost:5768/health || exit 1"],
        "interval": 30,
        "timeout": 5,
        "retries": 3,
        "startPeriod": 60
      }
    }
  ],
  "volumes": [
    {
      "name": "model-cache",
      "efsVolumeConfiguration": {
        "fileSystemId": "fs-12345678"
      }
    }
  ]
}
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: fastttsr
spec:
  replicas: 3
  selector:
    matchLabels:
      app: fastttsr
  template:
    metadata:
      labels:
        app: fastttsr
    spec:
      containers:
      - name: fastttsr
        image: fhrozen/fast-ttsr:latest
        ports:
        - containerPort: 5768
        env:
        - name: HTTP_PORT
          value: "5768"
        - name: MODEL_IDLE_TIMEOUT_SECONDS
          value: "60"
        resources:
          requests:
            memory: "1Gi"
            cpu: "1000m"
          limits:
            memory: "4Gi"
            cpu: "2000m"
        volumeMounts:
        - name: model-cache
          mountPath: /cache
        livenessProbe:
          httpGet:
            path: /health
            port: 5768
          initialDelaySeconds: 60
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health
            port: 5768
          initialDelaySeconds: 30
          periodSeconds: 10
      volumes:
      - name: model-cache
        persistentVolumeClaim:
          claimName: fastttsr-cache
```

---

## Configuration Best Practices

1. **Model Caching:**
   - Use persistent volumes for model cache
   - Pre-download models before first request
   - Share cache across instances (NFS, EFS, etc.)

2. **Memory Management:**
   - Set `MODEL_IDLE_TIMEOUT_SECONDS` based on usage patterns
   - High traffic: 60-120 seconds
   - Low traffic: 300-600 seconds
   - Development: 0 (never unload)

3. **Logging:**
   - Production: `Warning` or `Error`
   - Staging: `Information`
   - Development: `Debug`

4. **Resource Limits:**
   - Always set memory limits in production
   - Monitor actual usage and adjust
   - Kokoro Q4: 1-2 GB typical
   - Multiple models: 4-8 GB recommended

5. **High Availability:**
   - Deploy multiple instances
   - Use health checks with load balancer
   - Share model cache across instances
   - Monitor instance health

6. **Security:**
   - Never expose directly to internet
   - Use reverse proxy with SSL/TLS
   - Implement rate limiting
   - Set request size limits
   - Consider API key authentication
