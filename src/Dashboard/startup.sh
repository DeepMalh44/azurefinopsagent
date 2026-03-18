#!/bin/bash
# Startup script for Azure App Service (Linux) — installs tools for AI agent code execution
# Runs on every container start. pip packages are cached in /home (persistent across restarts).
# ⚠️ TEMPORARY: These tools run unsandboxed on the App Service.
# TODO: Migrate to Azure Container Apps dynamic sessions for secure, isolated execution.

echo "=== Azure FinOps Agent — Installing tools ==="

# ── System packages (reinstalls on each container start, typically <10s) ──
apt-get update -qq
apt-get install -y --no-install-recommends \
    python3 python3-pip \
    jq sqlite3 \
    2>/dev/null || true

# ── Python packages (cached in /home for persistence across restarts) ──
PIP_TARGET="/home/site/pip-packages"
mkdir -p "$PIP_TARGET"
export PYTHONPATH="$PIP_TARGET:$PYTHONPATH"

# Package list — bump PKG_VERSION when adding/removing packages to force reinstall
PACKAGES="requests pandas numpy openpyxl tabulate python-dateutil python-pptx matplotlib"
PKG_VERSION="3"

# Only install if marker version differs (forces reinstall on package list changes)
if [ ! -f "$PIP_TARGET/.installed_v$PKG_VERSION" ]; then
    echo "Installing Python packages (v$PKG_VERSION) to $PIP_TARGET..."
    # Clean stale markers and conflicting packages
    rm -f "$PIP_TARGET/.installed"* 2>/dev/null
    rm -rf "$PIP_TARGET/numpy"* "$PIP_TARGET/numpy.libs" 2>/dev/null
    pip3 install --no-cache-dir --break-system-packages --target "$PIP_TARGET" \
        $PACKAGES \
        2>/dev/null || true
    touch "$PIP_TARGET/.installed_v$PKG_VERSION"
    echo "Python packages installed and cached."
else
    echo "Python packages already cached (v$PKG_VERSION), skipping install."
fi

# Export PYTHONPATH so the .NET app's child processes (RunScript) inherit it
export PYTHONPATH="$PIP_TARGET:$PYTHONPATH"

echo "=== Tools ready ==="
python3 --version 2>/dev/null || echo "python3: not available"
jq --version 2>/dev/null || echo "jq: not available"
sqlite3 --version 2>/dev/null || echo "sqlite3: not available"

# Start the .NET app
cd /home/site/wwwroot
exec dotnet Dashboard.dll
