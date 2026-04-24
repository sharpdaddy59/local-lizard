#!/usr/bin/env bash
# LocalLizard Uninstaller
# Removes installed files, models, and optional systemd service.
# Run: ./uninstall.sh [--purge-models]
set -euo pipefail

INSTALL_DIR="${LIZARD_INSTALL_DIR:-/opt/local-lizard}"
PURGE_MODELS=false

for arg in "$@"; do
    case "$arg" in
        --purge-models) PURGE_MODELS=true ;;
        --help|-h) echo "Usage: $0 [--purge-models]"; exit 0 ;;
    esac
done

# Remove systemd service if installed
if systemctl is-active --quiet locallizard 2>/dev/null; then
    sudo systemctl stop locallizard
fi
if [[ -f /etc/systemd/system/locallizard.service ]]; then
    sudo systemctl disable locallizard
    sudo rm /etc/systemd/system/locallizard.service
    sudo systemctl daemon-reload
    echo "[OK] Systemd service removed"
fi

if [[ "$PURGE_MODELS" == true ]]; then
    echo "[INFO] Removing everything including models..."
    sudo rm -rf "$INSTALL_DIR"
    echo "[OK] ${INSTALL_DIR} removed completely"
else
    # Keep models, remove everything else
    if [[ -d "$INSTALL_DIR/models" ]]; then
        TEMP_MODELS=$(mktemp -d)
        cp -r "$INSTALL_DIR/models" "$TEMP_MODELS/"
        sudo rm -rf "$INSTALL_DIR"
        sudo mkdir -p "$INSTALL_DIR"
        sudo mv "$TEMP_MODELS/models" "$INSTALL_DIR/"
        rm -rf "$TEMP_MODELS"
        echo "[OK] Removed installation, kept models in ${INSTALL_DIR}/models/"
    else
        sudo rm -rf "$INSTALL_DIR"
        echo "[OK] ${INSTALL_DIR} removed"
    fi
fi

echo "[OK] Uninstall complete"
