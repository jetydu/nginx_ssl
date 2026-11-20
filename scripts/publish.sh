#!/usr/bin/env bash
# 构建前端并发布后端到 ./publish
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "==> 构建前端 (Vue 3)"
cd "$ROOT/web"
if [ -f package-lock.json ]; then
  npm ci
else
  npm install
fi
npm run build

echo "==> 发布后端 (.NET 10)"
cd "$ROOT"
rm -rf publish
dotnet publish src/NginxSslManager.Api/NginxSslManager.Api.csproj \
  -c Release \
  -o publish \
  --nologo

echo
echo "完成：$ROOT/publish"
echo "安装示例："
echo "  sudo mkdir -p /opt/nginx-ssl-manager /var/lib/nginx-ssl-manager"
echo "  sudo cp -r publish/* /opt/nginx-ssl-manager/"
echo "  sudo cp deploy/nginx-ssl-manager.service /etc/systemd/system/"
echo "  sudo systemctl daemon-reload && sudo systemctl enable --now nginx-ssl-manager"
