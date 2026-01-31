#!/usr/bin/env bash
# Bu script: 1) Paketi build eder, 2) Docker'daki NuGet sunucusuna (BaGet) push eder.
# Önce Docker sunucusunu başlatın: docker compose -f docker/docker-compose.yml up -d

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BAGET_URL="${BAGET_URL:-http://localhost:5555/v3/index.json}"
BAGET_API_KEY="${BAGET_API_KEY:-NUGET-SERVER-API-KEY}"

cd "$REPO_ROOT"

echo "==> Build (Release)..."
dotnet build -c Release --no-incremental
echo "==> Paket oluşturuluyor..."
dotnet pack -c Release --no-build -o "$REPO_ROOT/out/packages"

# En son oluşturulan paketi push et (versiyon sırasına göre değil, tarihe göre)
NUPKG=$(ls -t "$REPO_ROOT/out/packages"/*.nupkg 2>/dev/null | head -1)
if [ -z "$NUPKG" ]; then
  echo "Hata: .nupkg dosyası bulunamadı."
  exit 1
fi

echo "==> NuGet sunucusuna gönderiliyor: $BAGET_URL"
dotnet nuget push "$NUPKG" --api-key "$BAGET_API_KEY" --source "$BAGET_URL" --skip-duplicate

echo "==> Tamamlandı. Paket sunucuda. Tarayıcıda: ${BAGET_URL%/v3/index.json}/"
