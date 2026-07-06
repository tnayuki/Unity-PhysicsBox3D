#!/usr/bin/env bash
# Builds Box3D + the Unity shim into a single native plugin for the macOS Editor.
# No CMake required — Box3D is pure C with no external deps.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
BOX3D="$ROOT/box3d"
SHIM="$ROOT/shim/box3d_unity.c"
OUT_DIR="$ROOT/../Assets/Plugins/Box3D"
OUT="$OUT_DIR/box3d.dylib"

mkdir -p "$OUT_DIR"

# Build each arch separately so we can use arch-specific tuning, then lipo them
# into a universal dylib. -O3 + thin LTO measurably beats -O2 for the solver.
COMMON_FLAGS="-O3 -flto=thin -std=c17 -fvisibility=hidden -DNDEBUG -Dbox3d_EXPORTS"

clang -arch arm64 $COMMON_FLAGS -mcpu=apple-m1 \
	-I"$BOX3D/include" -I"$BOX3D/src" \
	-shared -o "$OUT.arm64" \
	"$BOX3D"/src/*.c "$SHIM" \
	-lpthread \
	-install_name @rpath/box3d.dylib

clang -arch x86_64 $COMMON_FLAGS \
	-I"$BOX3D/include" -I"$BOX3D/src" \
	-shared -o "$OUT.x86_64" \
	"$BOX3D"/src/*.c "$SHIM" \
	-lpthread \
	-install_name @rpath/box3d.dylib

lipo -create "$OUT.arm64" "$OUT.x86_64" -output "$OUT"
rm -f "$OUT.arm64" "$OUT.x86_64"

echo "Built $OUT"
lipo -info "$OUT" 2>/dev/null || true
nm -gU "$OUT" | grep -c "_b3u_" | xargs echo "shim symbols exported:"
