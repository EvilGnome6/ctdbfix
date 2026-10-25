#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
CUETOOLS_VERSION="v2.2.6"

if [ ! -d "external/cuetools.net" ]; then
  echo "Cloning cuetools.net ($CUETOOLS_VERSION)..."
  mkdir -p external
  git clone --branch "$CUETOOLS_VERSION" https://github.com/gchudov/cuetools.net.git external/cuetools.net
  (cd external/cuetools.net && git submodule update --init --recursive)
else
  echo "cuetools.net already present."
  (cd external/cuetools.net && git fetch origin && git checkout "$CUETOOLS_VERSION" && git submodule update --init --recursive)
fi

echo "Applying Mac/Linux compatibility patches..."
for p in patches/*.patch; do
  name=$(basename "$p")
  if patch -p0 -N --dry-run < "$p" >/dev/null 2>&1; then
    patch -p0 < "$p"
    echo "  - $name applied."
  else
    echo "  - $name already applied (or not needed)."
  fi
done
echo "Setup complete."
