FROM node:24-alpine AS frontend-build
WORKDIR /app/frontend
COPY frontend/package.json frontend/pnpm-workspace.yaml ./
RUN corepack enable pnpm && pnpm install --ignore-scripts --frozen-lockfile=false
COPY frontend/ ./
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS backend-build
WORKDIR /src
COPY SharpAudio.slnx ./
COPY src/SharpAudio.Api/SharpAudio.Api.csproj src/SharpAudio.Api/
COPY src/SharpAudio.Worker/SharpAudio.Worker.csproj src/SharpAudio.Worker/
COPY src/SharpAudio.Worker.Asr/SharpAudio.Worker.Asr.csproj src/SharpAudio.Worker.Asr/
RUN dotnet restore src/SharpAudio.Api/SharpAudio.Api.csproj
RUN dotnet restore src/SharpAudio.Worker/SharpAudio.Worker.csproj
RUN dotnet restore src/SharpAudio.Worker.Asr/SharpAudio.Worker.Asr.csproj
COPY src/SharpAudio.Api/ src/SharpAudio.Api/
COPY src/SharpAudio.Worker/ src/SharpAudio.Worker/
COPY src/SharpAudio.Worker.Asr/ src/SharpAudio.Worker.Asr/
COPY --from=frontend-build /app/frontend/dist/ src/SharpAudio.Api/wwwroot/
RUN dotnet publish src/SharpAudio.Api/SharpAudio.Api.csproj -c Release -o /out/api -r linux-x64 --self-contained false /p:UseAppHost=false
RUN dotnet publish src/SharpAudio.Worker/SharpAudio.Worker.csproj -c Release -o /out/worker -r linux-x64 --self-contained false /p:UseAppHost=true
RUN dotnet publish src/SharpAudio.Worker.Asr/SharpAudio.Worker.Asr.csproj -c Release -o /out/worker-asr -r linux-x64 --self-contained false /p:UseAppHost=true

# Shared base for all runtime image variants (TTS-only / ASR-only / both).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime-base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV MODEL_CACHE_DIR=/cache
ENV ESPEAK_DATA_DIR=/app/assets/espeak-ng-data

# espeak-ng (Kokoro TTS phonemization) + libgomp1 (Whisper.net's native ggml-cpu library needs OpenMP)
RUN apt-get update && \
    apt-get install -y --no-install-recommends libespeak-ng1 espeak-ng-data libgomp1 && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Note: /app/assets should be mounted as a volume with espeak-ng-data and tokens.txt
VOLUME ["/cache"]
COPY --from=backend-build /out/api/ ./

# --- TTS-only image: API + TTS worker only ---
# docker build --target runtime-tts -t fastttsr:tts .
FROM runtime-base AS runtime-tts
COPY --from=backend-build /out/worker/ ./worker/
RUN chmod +x ./worker/SharpAudio.Worker
ENV WorkerOptions__ExecutablePath=/app/worker/SharpAudio.Worker
ENV SERVER_MODE=tts
ENTRYPOINT ["dotnet", "SharpAudio.Api.dll"]

# --- ASR-only image: API + ASR worker only ---
# docker build --target runtime-asr -t fastttsr:asr .
FROM runtime-base AS runtime-asr
# ffmpeg normalizes any uploaded audio format (FLAC, MP3, OGG, WEBM, M4A, ...) to WAV before ASR.
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*
COPY --from=backend-build /out/worker-asr/ ./worker-asr/
RUN chmod +x ./worker-asr/SharpAudio.Worker.Asr
ENV AsrWorkerOptions__ExecutablePath=/app/worker-asr/SharpAudio.Worker.Asr
# Whisper.net.Runtime ships native libs under runtimes/<rid>/ (not the standard .../native/
# layout), so the dynamic linker won't find sibling .so dependencies without this on the path.
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64:/app/worker-asr/runtimes/linux-x64
ENV SERVER_MODE=asr
ENTRYPOINT ["dotnet", "SharpAudio.Api.dll"]

# --- Combined image: API + both workers (default if no --target is given) ---
# docker build -t fastttsr:all .   (equivalent to --target runtime-all)
FROM runtime-base AS runtime-all
# ffmpeg normalizes any uploaded audio format (FLAC, MP3, OGG, WEBM, M4A, ...) to WAV before ASR.
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*
COPY --from=backend-build /out/worker/ ./worker/
COPY --from=backend-build /out/worker-asr/ ./worker-asr/
RUN chmod +x ./worker/SharpAudio.Worker ./worker-asr/SharpAudio.Worker.Asr
ENV WorkerOptions__ExecutablePath=/app/worker/SharpAudio.Worker
ENV AsrWorkerOptions__ExecutablePath=/app/worker-asr/SharpAudio.Worker.Asr
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64:/app/worker-asr/runtimes/linux-x64
ENV SERVER_MODE=both
ENTRYPOINT ["dotnet", "SharpAudio.Api.dll"]
