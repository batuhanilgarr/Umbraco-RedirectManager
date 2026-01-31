# Bu script: 1) Paketi build eder, 2) Docker'daki NuGet sunucusuna (BaGet) push eder.
# Önce Docker sunucusunu başlatın: docker compose -f docker/docker-compose.yml up -d

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BagetUrl = if ($env:BAGET_URL) { $env:BAGET_URL } else { "http://localhost:5555/v3/index.json" }
$BagetApiKey = if ($env:BAGET_API_KEY) { $env:BAGET_API_KEY } else { "NUGET-SERVER-API-KEY" }

Set-Location $RepoRoot

$OutDir = Join-Path $RepoRoot "out\packages"
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

Write-Host "==> Build (Release)..."
dotnet build -c Release --no-incremental
Write-Host "==> Paket oluşturuluyor..."
dotnet pack -c Release --no-build -o $OutDir

$Nupkg = Get-ChildItem -Path $OutDir -Filter "*.nupkg" | Select-Object -First 1
if (-not $Nupkg) {
    Write-Host "Hata: .nupkg dosyası bulunamadı."
    exit 1
}

Write-Host "==> NuGet sunucusuna gönderiliyor: $BagetUrl"
dotnet nuget push $Nupkg.FullName --api-key $BagetApiKey --source $BagetUrl --skip-duplicate

Write-Host "==> Tamamlandı. Paket sunucuda."
