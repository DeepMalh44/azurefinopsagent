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

# Only install if marker file is missing (skip on subsequent restarts)
if [ ! -f "$PIP_TARGET/.installed" ]; then
    echo "Installing Python packages to $PIP_TARGET..."
    pip3 install --no-cache-dir --break-system-packages --target "$PIP_TARGET" \
        requests \
        pandas numpy \
        openpyxl \
        tabulate \
        python-dateutil \
        2>/dev/null || true
    touch "$PIP_TARGET/.installed"
    echo "Python packages installed and cached."
else
    echo "Python packages already cached, skipping install."
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
