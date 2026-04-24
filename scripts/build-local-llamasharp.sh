#!/usr/bin/env bash
# build-local-llamasharp.sh
# Builds LLamaSharp from source with updated llama.cpp backend (gemma4 support).
# Run from the LocalLizard repo root.
#
# Prerequisites: dotnet SDK 10, cmake, make, g++

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Config - adjust paths as needed
LLAMASHARP_REPO="${LLAMASHARP_REPO:-/tmp/LLamaSharp-src}"
LLAMA_COMMIT="${LLAMA_COMMIT:-15fa3c493}"  # commit with gemma4 arch support

NATIVE_LIBS_DIR="$REPO_ROOT/src/LocalLizard.LocalLLM/runtimes/linux-x64"

echo "=== Building custom LLamaSharp with gemma4 support ==="
echo "LLamaSharp repo: $LLAMASHARP_REPO"
echo "Target commit: $LLAMA_COMMIT"
echo ""

# Step 1: Ensure LLamaSharp is cloned
if [ ! -d "$LLAMASHARP_REPO" ]; then
    echo "Cloning LLamaSharp..."
    git clone https://github.com/SciSharp/LLamaSharp.git "$LLAMASHARP_REPO"
fi

cd "$LLAMASHARP_REPO"

# Step 2: Update llama.cpp submodule
echo "Updating llama.cpp submodule to $LLAMA_COMMIT..."
cd llama.cpp
git fetch origin 2>/dev/null || true
git checkout "$LLAMA_COMMIT" 2>/dev/null || {
    echo "Commit $LLAMA_COMMIT not found locally, fetching upstream..."
    git remote add upstream https://github.com/ggerganov/llama.cpp.git 2>/dev/null || true
    git fetch upstream
    git checkout "$LLAMA_COMMIT"
}
cd "$LLAMASHARP_REPO"

# Step 3: Build native shared libraries
echo "Building native .so libraries..."
cd llama.cpp
mkdir -p build
cd build
cmake .. \
    -DLLAMA_CUDA=OFF \
    -DLLAMA_VULKAN=OFF \
    -DBUILD_SHARED_LIBS=ON \
    -DLLAMA_BUILD_TESTS=OFF \
    -DLLAMA_BUILD_EXAMPLES=OFF \
    -DLLAMA_BUILD_SERVER=OFF \
    -DLLAMA_BUILD_LLAMA=ON \
    -DLLAMA_BUILD_COMMON=OFF \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON

make -j"$(nproc)"

cd "$LLAMASHARP_REPO"

# Step 4: Install native libs into LocalLizard project
echo ""
echo "Installing native libs to $NATIVE_LIBS_DIR..."

BUILD_OUT="$LLAMASHARP_REPO/llama.cpp/build/bin"
mkdir -p "$NATIVE_LIBS_DIR"
cp -av "$BUILD_OUT/libllama.so"*   "$NATIVE_LIBS_DIR/" 2>/dev/null || true
cp -av "$BUILD_OUT/libggml.so"*    "$NATIVE_LIBS_DIR/" 2>/dev/null || true
cp -av "$BUILD_OUT/libggml-base.so"* "$NATIVE_LIBS_DIR/" 2>/dev/null || true
cp -av "$BUILD_OUT/libggml-cpu.so"*  "$NATIVE_LIBS_DIR/" 2>/dev/null || true
cp -av "$BUILD_OUT/libmtmd.so"*    "$NATIVE_LIBS_DIR/" 2>/dev/null || true
echo "  -> $NATIVE_LIBS_DIR ($(ls -1 "$NATIVE_LIBS_DIR" | wc -l) files)"

# Step 5: Build LLamaSharp C# library
echo ""
echo "Building LLamaSharp C# library..."
dotnet build "$LLAMASHARP_REPO/LLama/LLamaSharp.csproj" -c Release 2>&1 | tail -3

# Step 6: Create local NuGet package
echo ""
echo "Creating local NuGet package..."
dotnet pack "$LLAMASHARP_REPO/LLama/LLamaSharp.csproj" -c Release -o "$REPO_ROOT/packages/" 2>&1 | tail -3

echo ""
echo "=== DONE ==="
echo "Custom LLamaSharp with gemma4 support is ready."
echo "Native libs at: $NATIVE_LIBS_DIR"
echo "NuGet package: $REPO_ROOT/packages/"
echo ""
echo "Next: run 'dotnet build' in 'src/LocalLizard.LocalLLM/' to build with gemma4 support."
