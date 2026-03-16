#!/bin/bash
# Startup script for Azure App Service — installs Python 3, pip, pandas, numpy, sqlite3
# ⚠️ TEMPORARY: These tools run unsandboxed on the App Service.
# TODO: Migrate to Azure Container Apps dynamic sessions for secure, isolated execution.

set -e

echo "=== Installing Python 3 + data tools ==="

# Install Python 3, pip, and sqlite3 (apt is available on Linux App Service)
apt-get update -qq
apt-get install -y -qq python3 python3-pip sqlite3 2>/dev/null || true

# Install Python data packages
pip3 install --quiet --break-system-packages pandas numpy 2>/dev/null || \
pip3 install --quiet pandas numpy 2>/dev/null || true

echo "=== Python version ==="
python3 --version 2>/dev/null || echo "python3 not available"

echo "=== pip packages ==="
pip3 list 2>/dev/null | grep -E "pandas|numpy" || echo "pip packages not available"

echo "=== sqlite3 version ==="
sqlite3 --version 2>/dev/null || echo "sqlite3 not available"

echo "=== Startup script complete ==="

# Start the .NET app
cd /home/site/wwwroot
dotnet Dashboard.dll
