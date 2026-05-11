FROM node:24-alpine AS frontend-build
WORKDIR /app/frontend
COPY frontend/package.json frontend/pnpm-workspace.yaml ./
RUN corepack enable pnpm && pnpm install --ignore-scripts --frozen-lockfile=false
COPY frontend/ ./
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS backend-build
WORKDIR /src
COPY FastTTSR.slnx ./
COPY src/FastTTSR.Api/FastTTSR.Api.csproj src/FastTTSR.Api/
RUN dotnet restore src/FastTTSR.Api/FastTTSR.Api.csproj
COPY src/FastTTSR.Api/ src/FastTTSR.Api/
COPY --from=frontend-build /app/frontend/dist/ src/FastTTSR.Api/wwwroot/
RUN dotnet publish src/FastTTSR.Api/FastTTSR.Api.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV MODEL_CACHE_DIR=/cache
VOLUME ["/cache"]
COPY --from=backend-build /out/ ./
ENTRYPOINT ["dotnet", "FastTTSR.Api.dll"]
