set -euo pipefail
cd "$(dirname "$0")/.."

RID="${1:-linux-x64}"
OUT="dist/PokerApp-${RID}"

dotnet publish PokerApp.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o "$OUT"

echo ""
echo "Gotowe: $OUT/PokerApp"
echo "Uruchomienie: $OUT/PokerApp"
