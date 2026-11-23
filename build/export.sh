#!/usr/bin/env bash
# ============================================================================
# One-command reproducible build of Matchday Eleven for Android (Unity 6 LTS).
#
#   bash build/export.sh              # signed arm64-v8a release APK
#
# Pinned: Unity 6000.0.58f1 editor, URP 17.0.4 (Forward+), IL2CPP, ARM64 only,
# ASTC texture compression, managed stripping, signed with the project key.
# Release output: Build/MatchdayEleven-arm64.apk (arm64-v8a only, signed).
#
# Environment:
#   PITCH_TOOLS     engine/tools cache        (default $HOME/.cache/pitch-unity-tools)
#   PITCH_OUTPUT    APK path                  (default Build/MatchdayEleven-arm64.apk)
#   PITCH_KEYSTORE  signing key              (default signing/debug.keystore)
#   PITCH_STOREPASS keystore password        (default android)
#   PITCH_KEYALIAS  signing key alias       (default androiddebugkey)
#   PITCH_KEYPASS   signing key password    (default android)
#   PITCH_ENFORCE_SIZE  =1 fails the build outside the 200-300 MB band
#
# The build runs the headless engine tests first, then the editor batch build
# that signs and inspects the APK (Build/build-facts.txt + Build/report.json).
# ============================================================================
set -euo pipefail
cd "$(dirname "$0")/.."

UNITY_VERSION="6000.0.58f1"
UNITY_URL="https://download.unity3d.com/download_unity/44b8bf3a3225/LinuxEditorInstaller/Unity-6000.0.58f1.tar.xz"
TOOLS="${PITCH_TOOLS:-$HOME/.cache/pitch-unity-tools}"
# CI runs in the official GameCI image, which already ships the pinned editor
# at /opt/unity/Editor/Unity. Prefer it and never download a second copy.
if [ -z "${UNITY_EDITOR:-}" ] && [ -x /opt/unity/Editor/Unity ]; then
  UNITY_EDITOR=/opt/unity/Editor/Unity
fi
UNITY_EDITOR="${UNITY_EDITOR:-$TOOLS/Editor/Unity}"

APK="${PITCH_OUTPUT:-Build/MatchdayEleven-arm64.apk}"

log() { printf '==> %s\n' "$*"; }

mkdir -p "$TOOLS" "$(dirname "$APK")"
START_EPOCH=$(date +%s)

# --- engine ----------------------------------------------------------------
# The Linux editor archive extracts to Editor/ at its root, so it is unpacked
# directly into $TOOLS: the binary is $TOOLS/Editor/Unity.
if [ ! -x "$UNITY_EDITOR" ]; then
  log "downloading Unity ${UNITY_VERSION} editor (Linux x86_64, ~4.2 GiB)"
  if [ ! -f "$TOOLS/unity-editor.tar.xz" ]; then
    curl -fsSL -o "$TOOLS/unity-editor.tar.xz.part" "$UNITY_URL"
    mv "$TOOLS/unity-editor.tar.xz.part" "$TOOLS/unity-editor.tar.xz"
  fi
  log "extracting the editor"
  tar -xJf "$TOOLS/unity-editor.tar.xz" -C "$TOOLS"
  chmod +x "$TOOLS/Editor/Unity"
fi
UNITY_EDITOR="${UNITY_EDITOR:-$TOOLS/Editor/Unity}"

# --- signing key -----------------------------------------------------------
export PITCH_KEYSTORE="${PITCH_KEYSTORE:-$PWD/signing/debug.keystore}"
export PITCH_STOREPASS="${PITCH_STOREPASS:-android}"
export PITCH_KEYALIAS="${PITCH_KEYALIAS:-androiddebugkey}"
export PITCH_KEYPASS="${PITCH_KEYPASS:-android}"

# --- generated assets ------------------------------------------------------
if [ ! -f Assets/Resources/Generated/Textures/grass_albedo.png ]; then
  log "generating deterministic PBR texture set (takes a few minutes)"
  # asset_generator.py uses the Python standard library only: no numpy, no pip.
  command -v python3 >/dev/null || { echo "python3 required"; exit 1; }
  python3 tools/asset_generator.py --out Assets/Resources/Generated/Textures --size 4096
fi

# --- engine tests ----------------------------------------------------------
log "running engine tests"
dotnet run --project tests/CoreTests 2>&1 | tail -20

# --- export ----------------------------------------------------------------
log "exporting signed Android arm64 APK with Unity ${UNITY_VERSION}"
# A previous batch-mode run can leave a lock behind.
rm -f Temp/UnityLockfile
# Unity 6 personal licences do not carry the com.unity.editor.headless
# entitlement, so a -nographics build fails with "build target was unsupported".
# Run the editor on a virtual display instead (the GameCI image ships xvfb).
UNITY_RUN=()
if [ -z "${DISPLAY:-}" ] && command -v xvfb-run >/dev/null; then
  UNITY_RUN=(xvfb-run --auto-servernum --server-args="-screen 0 1280x720x24")
fi
if ! PITCH_OUTPUT="$APK" "${UNITY_RUN[@]}" "$UNITY_EDITOR" \
  -batchmode -quit -projectPath "$PWD" \
  -executeMethod Pitch.EditorTools.BuildAndroid.BuildFromCli \
  -logFile "$TOOLS/unity-editor-export.log"; then
  echo "Unity export failed; last 120 lines of $TOOLS/unity-editor-export.log:"
  tail -120 "$TOOLS/unity-editor-export.log" || true
  exit 1
fi
if [ ! -f "$APK" ]; then
  echo "the Unity build did not produce $APK"
  tail -120 "$TOOLS/unity-editor-export.log" || true
  exit 1
fi

END_EPOCH=$(date +%s)
log "build time: $((END_EPOCH - START_EPOCH)) s"
log "APK: $APK"
wc -c "$APK" | awk '{printf "APK size: %.0f bytes (%.1f MiB)\n", $1, $1/1048576}'
sha256sum "$APK"
log "ABIs inside the APK:"
unzip -l "$APK" | grep "lib/" | awk '{print $4}' | sort -u
