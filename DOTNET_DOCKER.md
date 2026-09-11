# Running .NET Commands Without Local Installation

This project includes wrapper scripts that let you run any `dotnet` command through Docker without having .NET installed locally.

## Quick Start

### Linux/macOS

```bash
# Make script executable (one time)
chmod +x dotnet.sh

# Use like normal dotnet CLI
./dotnet.sh restore
./dotnet.sh build
./dotnet.sh test
./dotnet.sh run --project src/SharpAudio.Api
```

### Windows (PowerShell)

```powershell
# Use like normal dotnet CLI
.\dotnet.ps1 restore
.\dotnet.ps1 build
.\dotnet.ps1 test
.\dotnet.ps1 run --project src/SharpAudio.Api
```

## Common Commands

### Restore Packages

```bash
./dotnet.sh restore
```

### Build Projects

```bash
# Build entire solution
./dotnet.sh build

# Build specific project
./dotnet.sh build src/SharpAudio.Api/SharpAudio.Api.csproj

# Build in Release mode
./dotnet.sh build -c Release
```

### Run Application

```bash
# Run API project
./dotnet.sh run --project src/SharpAudio.Api

# Run with specific configuration
./dotnet.sh run --project src/SharpAudio.Api -c Release
```

### Run Tests

```bash
# Run all tests
./dotnet.sh test

# Run specific test project
./dotnet.sh test tests/SharpAudio.Api.Tests

# Run with verbose output
./dotnet.sh test -v normal
```

### Publish for Deployment

```bash
# Publish API
./dotnet.sh publish src/SharpAudio.Api/SharpAudio.Api.csproj -c Release -o ./publish/api

# Publish Worker
./dotnet.sh publish src/SharpAudio.Worker/SharpAudio.Worker.csproj -c Release -o ./publish/worker
```

### Clean Build Artifacts

```bash
./dotnet.sh clean
```

### Add NuGet Package

```bash
./dotnet.sh add src/SharpAudio.Api package Newtonsoft.Json
```

### Create New Project

```bash
./dotnet.sh new webapi -n MyNewProject
```

### Check Version

```bash
./dotnet.sh --version
```

### List SDKs and Runtimes

```bash
./dotnet.sh --list-sdks
./dotnet.sh --list-runtimes
```

## How It Works

The wrapper scripts:

1. **Run .NET SDK in Docker container** - Uses `mcr.microsoft.com/dotnet/sdk:10.0-preview`
2. **Mount current directory** - Your code is accessible at `/workspace` in the container
3. **Preserve file ownership** - Uses your user ID to avoid permission issues (Linux/macOS)
4. **Pass through all arguments** - Any dotnet command works exactly as expected
5. **Return exit codes** - Proper error handling and status reporting

## Configuration

### Change .NET Version

Edit the version in `dotnet.sh` or `dotnet.ps1`:

```bash
DOTNET_VERSION="10.0-preview"  # Change to 8.0, 9.0, etc.
```

### Use Different Base Image

```bash
IMAGE="mcr.microsoft.com/dotnet/sdk:10.0-preview"  # Change to aspnet, runtime, etc.
```

## Troubleshooting

### Permission Issues (Linux/macOS)

The script runs as your current user to avoid permission problems. If you still encounter issues:

```bash
# Fix ownership of generated files
sudo chown -R $USER:$USER .
```

### Slow First Run

The first run downloads the .NET SDK Docker image (~1GB). Subsequent runs are fast.

### Port Conflicts When Running

If running the app (`dotnet run`), expose ports:

```bash
# Modify dotnet.sh to add port mapping
docker run --rm \
  -p 8080:8080 \
  ...
```

Or use `docker-compose.yml` for running the full application.

## Advantages

✅ **No local installation** - Works on any machine with Docker  
✅ **Version consistency** - Everyone uses the exact same .NET version  
✅ **Clean environment** - No conflicts with other .NET installations  
✅ **Isolated dependencies** - NuGet packages cached in container  
✅ **Cross-platform** - Same experience on Windows, macOS, Linux  

## Limitations

⚠️ **Interactive commands** - Some interactive prompts may not work  
⚠️ **Performance** - Slightly slower than native .NET due to containerization  
⚠️ **Debugging** - IDE debugging requires local .NET SDK  
⚠️ **Hot reload** - `dotnet watch` may require additional configuration  

## For Development

For full development experience (debugging, IntelliSense, hot reload), consider:

1. **Install .NET SDK locally** - Best developer experience
2. **Use Dev Containers** - VS Code Remote Containers extension
3. **Use these scripts for CI/CD** - Consistent build environment

## Integration with CI/CD

These scripts work great in CI/CD pipelines:

```yaml
# GitHub Actions example
- name: Restore
  run: ./dotnet.sh restore

- name: Build
  run: ./dotnet.sh build -c Release

- name: Test
  run: ./dotnet.sh test
```

## Alternative: Docker Compose

For running the full application, use `docker-compose.yml`:

```bash
docker compose build
docker compose up -d
```

This is better for running the actual service, while `dotnet.sh` is better for development tasks.
