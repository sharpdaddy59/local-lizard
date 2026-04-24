#!/usr/bin/env bash
#
# LocalLizard One-Click Installer
# Sets up everything on a fresh Ubuntu (24.04+) system.
#
# Usage:
#   chmod +x install.sh
#   ./install.sh [--skip-dotnet] [--skip-models] [--skip-piper] [--skip-whisper]
#
# Environment variables (optional):
#   LIZARD_INSTALL_DIR  — Where to install (default: /opt/local-lizard)
#   LIZARD_MODEL_URL    — Custom GGUF model download URL
#   LIZARD_PIPER_VOICE  — Piper voice name (default: hfc_female)
#
set -euo pipefail

INSTALL_DIR="${LIZARD_INSTALL_DIR:-/opt/local-lizard}"
SKIP_DOTNET=false
SKIP_MODELS=false
SKIP_PIPER=false
SKIP_WHISPER=false

# Parse args
for arg in "$@"; do
    case "$arg" in
        --skip-dotnet)  SKIP_DOTNET=true ;;
        --skip-models)  SKIP_MODELS=true ;;
        --skip-piper)   SKIP_PIPER=true ;;
        --skip-whisper) SKIP_WHISPER=true ;;
        --help|-h)
            echo "Usage: $0 [--skip-dotnet] [--skip-models] [--skip-piper] [--skip-whisper]"
            echo ""
            echo "Installs LocalLizard and all dependencies on fresh Ubuntu 24.04+."
            exit 0 ;;
        *)
            echo "Unknown argument: $arg"
            exit 1 ;;
    esac
done

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${CYAN}[INFO]${NC}  $*"; }
ok()    { echo -e "${GREEN}[OK]${NC}    $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
fail()  { echo -e "${RED}[FAIL]${NC}  $*"; exit 1; }

# ─── Preflight ────────────────────────────────────────────────────────────────

info "LocalLizard Installer"
info "Target directory: ${INSTALL_DIR}"
echo ""

# Check OS
if [[ ! -f /etc/os-release ]]; then
    fail "Cannot detect OS. This installer targets Ubuntu 24.04+."
fi
source /etc/os-release
if [[ "$ID" != "ubuntu" ]] || dpkg --compare-versions "${VERSION_ID}" "lt" "24.04"; then
    warn "Detected OS: ${PRETTY_NAME}. This installer is tested on Ubuntu 24.04+."
    warn "Proceeding anyway — your mileage may vary."
fi

# Check architecture
ARCH=$(uname -m)
if [[ "$ARCH" != "x86_64" && "$ARCH" != "aarch64" ]]; then
    fail "Unsupported architecture: ${ARCH}. Only x86_64 and aarch64 are supported."
fi
info "Architecture: ${ARCH}"

# ─── System Dependencies ─────────────────────────────────────────────────────

info "Installing system dependencies..."
sudo apt-get update -qq
sudo apt-get install -y -qq \
    build-essential cmake git wget curl \
    libasound2-dev libpulse-dev \
    ffmpeg \
    > /dev/null
ok "System dependencies installed"

# ─── .NET 10 SDK ─────────────────────────────────────────────────────────────

if [[ "$SKIP_DOTNET" == true ]]; then
    warn "Skipping .NET SDK installation (--skip-dotnet)"
else
    if command -v dotnet &>/dev/null && dotnet --version | grep -qE '^10\.'; then
        ok ".NET SDK $(dotnet --version) already installed"
    else
        info "Installing .NET 10 SDK..."
        # Install via Microsoft feed
        sudo apt-get install -y -qq apt-transport-https > /dev/null 2>&1 || true
        
        # Use install script (works across distros)
        curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --install-dir /usr/local/share/dotnet
        
        # Add to PATH if not already there
        DOTNET_PATH="/usr/local/share/dotnet"
        if ! grep -q "$DOTNET_PATH" /etc/profile.d/dotnet.sh 2>/dev/null; then
            echo "export PATH=\"\$PATH:${DOTNET_PATH}\"" | sudo tee /etc/profile.d/dotnet.sh > /dev/null
            sudo chmod +x /etc/profile.d/dotnet.sh
        fi
        export PATH="$PATH:${DOTNET_PATH}"
        
        if command -v dotnet &>/dev/null; then
            ok ".NET SDK $(dotnet --version) installed"
        else
            fail "Failed to install .NET SDK"
        fi
    fi
fi

# ─── Create directory structure ──────────────────────────────────────────────

info "Setting up directories..."
sudo mkdir -p "${INSTALL_DIR}"/{src,models,bin}
sudo mkdir -p "${INSTALL_DIR}/models/whisper"
sudo mkdir -p "${INSTALL_DIR}/voices"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ─── Build LocalLizard ──────────────────────────────────────────────────────

if [[ -d "${SCRIPT_DIR}/src" ]]; then
    info "Building LocalLizard from source..."
    
    # Copy source if installing from repo
    if [[ "$SCRIPT_DIR" != "$INSTALL_DIR" ]]; then
        sudo cp -r "${SCRIPT_DIR}/src/"* "${INSTALL_DIR}/src/"
    fi
    
    # Create solution file if it doesn't exist
    if [[ ! -f "${INSTALL_DIR}/src/LocalLizard.sln" ]]; then
        cat > "${INSTALL_DIR}/src/LocalLizard.sln" << 'SLNEOF'
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LocalLizard.Common", "LocalLizard.Common\LocalLizard.Common.csproj", "{A1B2C3D4-0001-0001-0001-000000000001}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LocalLizard.LocalLLM", "LocalLizard.LocalLLM\LocalLizard.LocalLLM.csproj", "{A1B2C3D4-0002-0002-0002-000000000002}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LocalLizard.Voice", "LocalLizard.Voice\LocalLizard.Voice.csproj", "{A1B2C3D4-0003-0003-0003-000000000003}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LocalLizard.Web", "LocalLizard.Web\LocalLizard.Web.csproj", "{A1B2C3D4-0004-0004-0004-000000000004}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LocalLizard.Telegram", "LocalLizard.Telegram\LocalLizard.Telegram.csproj", "{A1B2C3D4-0005-0005-0005-000000000005}"
EndProject
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
        Release|Any CPU = Release|Any CPU
    EndGlobalSection
    GlobalSection(ProjectConfigurationPlatforms) = postSolution
        {A1B2C3D4-0001-0001-0001-000000000001}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {A1B2C3D4-0001-0001-0001-000000000001}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {A1B2C3D4-0001-0001-0001-000000000001}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {A1B2C3D4-0001-0001-0001-000000000001}.Release|Any CPU.Build.0 = Release|Any CPU
        {A1B2C3D4-0002-0002-0002-000000000002}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {A1B2C3D4-0002-0002-0002-000000000002}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {A1B2C3D4-0002-0002-0002-000000000002}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {A1B2C3D4-0002-0002-0002-000000000002}.Release|Any CPU.Build.0 = Release|Any CPU
        {A1B2C3D4-0003-0003-0003-000000000003}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {A1B2C3D4-0003-0003-0003-000000000003}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {A1B2C3D4-0003-0003-0003-000000000003}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {A1B2C3D4-0003-0003-0003-000000000003}.Release|Any CPU.Build.0 = Release|Any CPU
        {A1B2C3D4-0004-0004-0004-000000000004}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {A1B2C3D4-0004-0004-0004-000000000004}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {A1B2C3D4-0004-0004-0004-000000000004}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {A1B2C3D4-0004-0004-0004-000000000004}.Release|Any CPU.Build.0 = Release|Any CPU
        {A1B2C3D4-0005-0005-0005-000000000005}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {A1B2C3D4-0005-0005-0005-000000000005}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {A1B2C3D4-0005-0005-0005-000000000005}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {A1B2C3D4-0005-0005-0005-000000000005}.Release|Any CPU.Build.0 = Release|Any CPU
    EndGlobalSection
EndGlobal
SLNEOF
    fi
    
    cd "${INSTALL_DIR}/src"
    dotnet build -c Release --nologo -v q 2>&1 | tail -1
    ok "Build complete"
else
    warn "No src/ directory found at ${SCRIPT_DIR}. Skipping build."
fi

# ─── Whisper Model ───────────────────────────────────────────────────────────

if [[ "$SKIP_WHISPER" == true ]]; then
    warn "Skipping whisper model download (--skip-whisper)"
else
    WHISPER_MODEL="${INSTALL_DIR}/models/whisper/ggml-base.bin"
    if [[ -f "$WHISPER_MODEL" ]]; then
        ok "Whisper model already exists (${WHISPER_MODEL})"
    else
        info "Downloading Whisper base model (~148MB)..."
        wget -q --show-progress -O "$WHISPER_MODEL" \
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
        ok "Whisper model downloaded"
    fi
fi

# ─── Piper TTS ───────────────────────────────────────────────────────────────

if [[ "$SKIP_PIPER" == true ]]; then
    warn "Skipping Piper installation (--skip-piper)"
else
    PIPER_DIR="${INSTALL_DIR}/bin/piper"
    PIPER_VOICE="${LIZARD_PIPER_VOICE:-hfc_female}"
    
    if [[ -x "${PIPER_DIR}/piper" ]]; then
        ok "Piper already installed at ${PIPER_DIR}"
    else
        info "Installing Piper TTS..."
        mkdir -p "$PIPER_DIR"
        
        # Determine arch suffix
        if [[ "$ARCH" == "x86_64" ]]; then
            PIPER_ARCH="amd64"
        else
            PIPER_ARCH="arm64"
        fi
        
        PIPER_TARBALL="piper_linux_${PIPER_ARCH}.tar.gz"
        PIPER_URL="https://github.com/rhasspy/piper/releases/latest/download/${PIPER_TARBALL}"
        
        wget -q --show-progress -O /tmp/piper.tar.gz "$PIPER_URL"
        tar xzf /tmp/piper.tar.gz -C "$PIPER_DIR" --strip-components=1
        rm -f /tmp/piper.tar.gz
        chmod +x "${PIPER_DIR}/piper"
        ok "Piper installed"
    fi
    
    # Download voice model
    PIPER_VOICE_DIR="${INSTALL_DIR}/voices"
    PIPER_ONNX="${PIPER_VOICE_DIR}/${PIPER_VOICE}.onnx"
    
    if [[ -f "$PIPER_ONNX" ]]; then
        ok "Piper voice model already exists"
    else
        info "Downloading Piper voice model (${PIPER_VOICE})..."
        # HFC voices from piper's training dataset
        VOICE_BASE="https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/${PIPER_VOICE}/medium"
        wget -q --show-progress -O "${PIPER_ONNX}" "${VOICE_BASE}/en_US-${PIPER_VOICE}-medium.onnx" || {
            # Try alternate path
            VOICE_BASE="https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/hfc_female/medium"
            wget -q --show-progress -O "${PIPER_ONNX}" "${VOICE_BASE}/en_US-hfc_female-medium.onnx"
        }
        wget -q --show-progress -O "${PIPER_ONNX}.json" "${VOICE_BASE}/en_US-${PIPER_VOICE}-medium.onnx.json" 2>/dev/null || true
        ok "Piper voice model downloaded"
    fi
fi

# ─── LLM Model ───────────────────────────────────────────────────────────────

if [[ "$SKIP_MODELS" == true ]]; then
    warn "Skipping LLM model download (--skip-models)"
else
    MODEL_PATH="${INSTALL_DIR}/models/gemma-3-1b-it-Q4_K_M.gguf"
    if [[ -f "$MODEL_PATH" ]]; then
        ok "LLM model already exists (${MODEL_PATH})"
    else
        MODEL_URL="${LIZARD_MODEL_URL:-https://huggingface.co/google/gemma-3-1b-it-qat-q4_k_m-gguf/resolve/main/gemma-3-1b-it-qat-q4_k_m.gguf}"
        info "Downloading Gemma 3 1B GGUF model (~900MB)..."
        info "URL: ${MODEL_URL}"
        wget -q --show-progress -c -O "$MODEL_PATH" "$MODEL_URL"
        ok "LLM model downloaded"
    fi
fi

# ─── Configuration ───────────────────────────────────────────────────────────

CONFIG_FILE="${INSTALL_DIR}/locallizard.env"

info "Generating configuration..."
cat > "$CONFIG_FILE" << EOF
# LocalLizard Configuration
# Edit this file to customize your setup

# LLM Model
LIZARD_MODEL_PATH=${INSTALL_DIR}/models/gemma-3-1b-it-Q4_K_M.gguf

# Whisper (using Whisper.net — no separate binary needed)
LIZARD_WHISPER_MODEL_PATH=${INSTALL_DIR}/models/whisper/ggml-base.bin

# Piper TTS
LIZARD_PIPER_PATH=${INSTALL_DIR}/bin/piper/piper
LIZARD_PIPER_MODEL=${INSTALL_DIR}/voices/hfc_female.onnx

# Wake word
LIZARD_WAKE_PHRASE=hey lizard

# Telegram (set your bot token here)
LIZARD_TELEGRAM_BOT_TOKEN=
EOF
ok "Configuration written to ${CONFIG_FILE}"

# ─── Launch Script ────────────────────────────────────────────────────────────

LAUNCH_SCRIPT="${INSTALL_DIR}/locallizard.sh"
cat > "$LAUNCH_SCRIPT" << 'LAUNCHEOF'
#!/usr/bin/env bash
# LocalLizard Launcher
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/locallizard.env"
export LIZARD_MODEL_PATH LIZARD_WHISPER_MODEL_PATH LIZARD_PIPER_PATH LIZARD_PIPER_MODEL
export LIZARD_WAKE_PHRASE LIZARD_TELEGRAM_BOT_TOKEN

MODE="${1:-web}"

case "$MODE" in
    web)
        echo "Starting LocalLizard Web UI on http://localhost:5000"
        dotnet run --project "${SCRIPT_DIR}/src/LocalLizard.Web" --no-launch-profile -- --urls "http://0.0.0.0:5000"
        ;;
    telegram)
        if [[ -z "$LIZARD_TELEGRAM_BOT_TOKEN" ]]; then
            echo "ERROR: Set LIZARD_TELEGRAM_BOT_TOKEN in ${SCRIPT_DIR}/locallizard.env"
            exit 1
        fi
        echo "Starting LocalLizard Telegram Bot..."
        dotnet run --project "${SCRIPT_DIR}/src/LocalLizard.Telegram" --no-launch-profile
        ;;
    console)
        echo "Starting LocalLizard Console (LLM only)..."
        dotnet run --project "${SCRIPT_DIR}/src/LocalLizard.LocalLLM" --no-launch-profile
        ;;
    *)
        echo "Usage: $0 {web|telegram|console}"
        echo ""
        echo "  web       — Web UI with chat + voice (default)"
        echo "  telegram  — Telegram bot interface"
        echo "  console   — Text-only LLM console"
        exit 1
        ;;
esac
LAUNCHEOF
chmod +x "$LAUNCH_SCRIPT"
ok "Launch script created: ${LAUNCH_SCRIPT}"

# ─── Systemd Service (optional) ──────────────────────────────────────────────

read -p "Install as systemd service? (starts on boot) [y/N] " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    SERVICE_FILE="/etc/systemd/system/locallizard.service"
    sudo tee "$SERVICE_FILE" > /dev/null << EOF
[Unit]
Description=LocalLizard AI Assistant
After=network.target sound.target

[Service]
Type=simple
User=$USER
WorkingDirectory=${INSTALL_DIR}
ExecStart=${LAUNCH_SCRIPT} web
Restart=on-failure
RestartSec=10
EnvironmentFile=${CONFIG_FILE}

[Install]
WantedBy=multi-user.target
EOF
    sudo systemctl daemon-reload
    sudo systemctl enable locallizard
    ok "Systemd service installed. Run: sudo systemctl start locallizard"
fi

# ─── Done ─────────────────────────────────────────────────────────────────────

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
ok "LocalLizard installation complete!"
echo ""
info "Next steps:"
echo "  1. Edit ${CONFIG_FILE} to set your preferences"
echo "  2. Run: ${LAUNCH_SCRIPT} web"
echo "  3. Open http://localhost:5000 in your browser"
echo ""
info "Other modes:"
echo "  ${LAUNCH_SCRIPT} telegram   — Telegram bot"
echo "  ${LAUNCH_SCRIPT} console    — Text-only LLM"
echo ""
info "Hardware acceleration:"
echo "  For AMD GPU: set LlmGpuLayers=99 in your code config"
echo "  For NVIDIA: install CUDA toolkit, use LLamaSharp.Backend.Cuda"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
