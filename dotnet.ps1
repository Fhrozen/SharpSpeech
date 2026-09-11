# Universal .NET CLI wrapper using Docker (PowerShell version)
# Usage: .\dotnet.ps1 [any dotnet command]
# Examples:
#   .\dotnet.ps1 restore
#   .\dotnet.ps1 build
#   .\dotnet.ps1 test
#   .\dotnet.ps1 run --project src/SharpAudio.Api
#   .\dotnet.ps1 publish -c Release

param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"

# Configuration
$DOTNET_VERSION = "10.0-preview"
$IMAGE = "mcr.microsoft.com/dotnet/sdk:$DOTNET_VERSION"

# Show what we're running
Write-Host "🐳 Running: dotnet $($Arguments -join ' ')" -ForegroundColor Blue
Write-Host ""

# Get current directory (cross-platform path)
$CurrentDir = (Get-Location).Path

# Create local cache directories if they don't exist
$CacheDir = ".docker-cache"
New-Item -ItemType Directory -Force -Path "$CacheDir/nuget" | Out-Null
New-Item -ItemType Directory -Force -Path "$CacheDir/dotnet" | Out-Null

# Run dotnet command in container with persistent caches
$dockerArgs = @(
    "run", "--rm",
    "-v", "${CurrentDir}:/workspace",
    "-v", "${CurrentDir}/${CacheDir}/nuget:/tmp/.nuget",
    "-v", "${CurrentDir}/${CacheDir}/dotnet:/tmp/.dotnet",
    "-w", "/workspace",
    "-e", "DOTNET_CLI_HOME=/tmp/.dotnet",
    "-e", "HOME=/tmp",
    "-e", "NUGET_PACKAGES=/tmp/.nuget/packages",
    "-e", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1",
    "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
    $IMAGE,
    "dotnet"
) + $Arguments

& docker $dockerArgs

$ExitCode = $LASTEXITCODE

Write-Host ""
if ($ExitCode -eq 0) {
    Write-Host "✅ Command completed successfully" -ForegroundColor Green
} else {
    Write-Host "❌ Command failed with exit code $ExitCode" -ForegroundColor Red
    exit $ExitCode
}
