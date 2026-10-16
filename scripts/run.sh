#!/usr/bin/env bash
# Abre o DirectPackageInstaller (Desktop) a partir do código-fonte.
#
# Uso:
#   scripts/run.sh                 compila (Debug) e abre o app
#   scripts/run.sh --release       compila em Release
#   scripts/run.sh --build-only    só compila, não abre
#   scripts/run.sh -- <args>       repassa argumentos ao app
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/DirectPackageInstaller/DirectPackageInstaller.Desktop/DirectPackageInstaller.Desktop.csproj"

CONFIG="Debug"
BUILD_ONLY=0
APP_ARGS=()

while [ $# -gt 0 ]; do
  case "$1" in
    --release)    CONFIG="Release" ;;
    --build-only) BUILD_ONLY=1 ;;
    -h|--help)    sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    --)           shift; APP_ARGS=("$@"); break ;;
    *)            echo "Opção desconhecida: $1 (use --help)" >&2; exit 2 ;;
  esac
  shift
done

# Procura um SDK do .NET 8: DOTNET_ROOT, ~/.dotnet8 e, por fim, o dotnet do sistema.
has_sdk8() { "$1" --list-sdks 2>/dev/null | grep -q '^8\.'; }

DOTNET=""
for candidate in "${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}" "$HOME/.dotnet8/dotnet" "$(command -v dotnet || true)"; do
  if [ -n "$candidate" ] && [ -x "$candidate" ] && has_sdk8 "$candidate"; then
    DOTNET="$candidate"
    break
  fi
done

if [ -z "$DOTNET" ]; then
  cat >&2 <<'MSG'
SDK do .NET 8 não encontrado.
Instale com:
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet8"
MSG
  exit 1
fi

export DOTNET_ROOT="$(dirname "$DOTNET")"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# Os submódulos são necessários para compilar.
if [ ! -e "$ROOT/LibOrbisPkg/.git" ]; then
  echo "Inicializando submódulos..."
  git -C "$ROOT" submodule update --init --depth 1
fi

echo "Compilando ($CONFIG) com $("$DOTNET" --version)..."
"$DOTNET" build "$PROJECT" -c "$CONFIG" --nologo -v q -clp:ErrorsOnly

if [ "$BUILD_ONLY" -eq 1 ]; then
  echo "Build concluído."
  exit 0
fi

echo "Abrindo o DirectPackageInstaller..."
exec "$DOTNET" run --project "$PROJECT" -c "$CONFIG" --no-build -- ${APP_ARGS[@]+"${APP_ARGS[@]}"}
