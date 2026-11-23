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
# The build is bounded. Every stage is timed and written to
# Build/stage-timings.txt, the Unity editor runs under `timeout` (60m by
# default) with its log streamed to stdout instead of hidden in a file, and a
# timeout prints the last lines of the log before the script fails. A hang can
# therefore not silently burn hours of runner time.
#
# Environment:
#   PITCH_TOOLS         engine/tools cache        (default $HOME/.cache/pitch-unity-tools)
#   PITCH_OUTPUT        APK path                  (default Build/MatchdayEleven-arm64.apk)
#   PITCH_KEYSTORE      signing key               (default signing/debug.keystore)
#   PITCH_STOREPASS     keystore password         (default android)
#   PITCH_KEYALIAS      signing key alias         (default androiddebugkey)
#   PITCH_KEYPASS       signing key password      (default android)
#   PITCH_ENFORCE_SIZE  =1 fails outside the 200-300 MB band
#   PITCH_TEXTURE_SIZE  generated texture page    (default 4096; the size band
#                       needs 4096 ASTC 6x6 pages, generation takes ~35 s and is
#                       cached by the workflow)
#   PITCH_UNITY_TIMEOUT timeout for one Unity run (default 60m)
#   PITCH_UNITY_MODE    batch | gui | auto        (default auto: batch mode
#                       first, one GUI retry if the licence lacks the headless
#                       entitlement)
#   PITCH_UNITY_LOG     Unity log file            (default $PITCH_TOOLS/unity-editor-export.log)
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
TEXTURE_SIZE="${PITCH_TEXTURE_SIZE:-4096}"
TEXTURE_DIR="Assets/Resources/Generated/Textures"
STAGE_LOG="${PITCH_STAGE_LOG:-Build/stage-timings.txt}"
UNITY_LOG="${PITCH_UNITY_LOG:-$TOOLS/unity-editor-export.log}"
PREFLIGHT_LOG="$TOOLS/unity-preflight.log"
UNITY_TIMEOUT="${PITCH_UNITY_TIMEOUT:-60m}"
UNITY_MODE="${PITCH_UNITY_MODE:-auto}"
LOG_DIR="Build/logs"

JOB_START=$(date +%s)
mkdir -p "$TOOLS" Build "$LOG_DIR" "$(dirname "$APK")"
: > "$STAGE_LOG"

elapsed() { echo $(( $(date +%s) - JOB_START )); }
log() { printf '[+%4ss] %s\n' "$(elapsed)" "$*"; }

STAGE="startup"
STAGE_START=$JOB_START
STAGE_OPEN=1
begin_stage() {
  STAGE="$1"
  STAGE_START=$(date +%s)
  STAGE_OPEN=1
  log "--- stage: $STAGE ---"
}
end_stage() {
  local dt=$(( $(date +%s) - STAGE_START ))
  STAGE_OPEN=0
  printf '%-22s %6ss\n' "$STAGE" "$dt" | tee -a "$STAGE_LOG"
}

finish() {
  local code=$?
  trap - EXIT
  if [ "$STAGE_OPEN" = "1" ] && [ "$code" -ne 0 ]; then
    local dt=$(( $(date +%s) - STAGE_START ))
    printf '!! stage %-15s did not finish, ran %ss (exit %s)\n' "$STAGE" "$dt" "$code" | tee -a "$STAGE_LOG"
  fi
  # Keep the editor logs next to the build so the workflow can upload them even
  # when this script failed, timed out, or the job is cancelled afterwards.
  cp -f "$UNITY_LOG" "$LOG_DIR/" 2>/dev/null || true
  cp -f "$PREFLIGHT_LOG" "$LOG_DIR/" 2>/dev/null || true
  echo
  echo "Stage timings ($STAGE_LOG):"
  cat "$STAGE_LOG" 2>/dev/null || true
  echo "total: $(elapsed)s"
  exit "$code"
}
trap finish EXIT

# --- engine ----------------------------------------------------------------
begin_stage environment
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
log "editor: $UNITY_EDITOR ($UNITY_VERSION)"
log "cpus: $(nproc), memory: $(free -m | awk '/Mem:/{print $2" MiB"}')"
df -h . | tail -1 | awk '{print "[+] disk available: "$4}'
if [ -z "${DISPLAY:-}" ]; then
  if command -v xvfb-run >/dev/null; then
    log "no DISPLAY: Unity runs on Xvfb (a display is required, see below)"
  else
    log "no DISPLAY and no xvfb-run: Unity may refuse to start"
  fi
fi
log "unity timeout per attempt: $UNITY_TIMEOUT (mode: $UNITY_MODE)"
end_stage

# --- signing key -----------------------------------------------------------
export PITCH_KEYSTORE="${PITCH_KEYSTORE:-$PWD/signing/debug.keystore}"
export PITCH_STOREPASS="${PITCH_STOREPASS:-android}"
export PITCH_KEYALIAS="${PITCH_KEYALIAS:-androiddebugkey}"
export PITCH_KEYPASS="${PITCH_KEYPASS:-android}"

# --- generated assets ------------------------------------------------------
# 20 pages at 4096 px in pure Python take ~35 s (measured: 02:31:38 -> 02:32:11
# in run 45100332560). It is not the stall, and it cannot be shrunk to 1024/2048
# without leaving the 200-300 MB band: ASTC 6x6 at 4096 is where the payload
# comes from. The workflow caches Assets/Resources/Generated, so this only runs
# once per generator change.
begin_stage textures
if [ -f "$TEXTURE_DIR/manifest.json" ] && [ -f "$TEXTURE_DIR/grass_albedo.png" ]; then
  log "generated textures already present (cache hit): $(ls -1 "$TEXTURE_DIR"/*.png 2>/dev/null | wc -l) pages"
else
  log "generating deterministic PBR texture set at ${TEXTURE_SIZE}px (pure Python, ~35 s)"
  command -v python3 >/dev/null || { echo "python3 required"; exit 1; }
  python3 tools/asset_generator.py --out "$TEXTURE_DIR" --size "$TEXTURE_SIZE" | tail -6
fi
end_stage

# --- engine tests ----------------------------------------------------------
begin_stage engine-tests
dotnet run --project tests/CoreTests 2>&1 | tail -20
end_stage

# --- licence / target preflight --------------------------------------------
# Fail before the long build, not after it: this opens the project in batch mode
# (which needs an active licence) and prints whether the Android target and its
# module are usable. It also warms Library/, so the export below does not pay
# the first import twice.
begin_stage unity-preflight
launcher=()
if [ -z "${DISPLAY:-}" ] && command -v xvfb-run >/dev/null; then
  launcher=(xvfb-run --auto-servernum --server-args="-screen 0 1280x720x24")
fi
preflight=( "$UNITY_EDITOR" -batchmode -accept-apiupdate -quit -projectPath "$PWD"
            -executeMethod Pitch.EditorTools.BuildAndroid.Probe -logFile - )
: > "$PREFLIGHT_LOG"
set +e
timeout --signal=INT --kill-after=60s 15m "${launcher[@]}" "${preflight[@]}" 2>&1 \
  | python3 tools/unity_log.py --log "$PREFLIGHT_LOG" --idle-warn 120
PREFLIGHT_RC=${PIPESTATUS[0]}
set -e
if [ "$PREFLIGHT_RC" -ne 0 ]; then
  echo "=== last 60 lines of $PREFLIGHT_LOG ==="
  tail -60 "$PREFLIGHT_LOG" || true
  log "::error::Unity preflight failed (exit $PREFLIGHT_RC): the licence is not active or the editor cannot start"
  exit 1
fi
LOG_LABEL="licence-ok"
if grep -q "PROBE android_supported=false" "$PREFLIGHT_LOG"; then
  grep "PROBE " "$PREFLIGHT_LOG" || true
  log "::error::the Android build target is not usable in this editor (missing AndroidPlayer module)"
  exit 1
fi
grep -q "PROBE android_supported=true" "$PREFLIGHT_LOG" || {
  tail -40 "$PREFLIGHT_LOG" || true
  log "::error::the preflight did not report the Android target; see the log above"
  exit 1
}
grep "PROBE " "$PREFLIGHT_LOG" | sed 's/^/    /' || true
if grep -qiE "No valid Unity Editor license|License is not active|Failed to activate" "$PREFLIGHT_LOG"; then
  log "::error::Unity reports an inactive licence; refusing to start the long build"
  exit 1
fi
end_stage

# --- export ----------------------------------------------------------------
# One Unity run = one attempt. The log is streamed (logFile -) through the
# stage filter, so the CI output shows which stage is running and how long it
# has been silent. The editor gets SIGINT on timeout, which lets it flush and
# shut down instead of leaving a broken Library/ behind.
unity_attempt() {
  local mode="$1" tmo="$2"
  local -a args=( "$UNITY_EDITOR" )
  [ "$mode" = "batch" ] && args+=( -batchmode )
  args+=( -accept-apiupdate -quit -projectPath "$PWD" -buildTarget Android
          -executeMethod Pitch.EditorTools.BuildAndroid.BuildFromCli -logFile - )
  local -a display=()
  if [ -z "${DISPLAY:-}" ] && command -v xvfb-run >/dev/null; then
    display=(xvfb-run --auto-servernum --server-args="-screen 0 1280x720x24")
  fi
  : > "$UNITY_LOG"
  log "unity export: mode=$mode timeout=$tmo"
  set +e
  timeout --signal=INT --kill-after=2m "$tmo" "${display[@]}" "${args[@]}" 2>&1 \
    | python3 tools/unity_log.py --log "$UNITY_LOG" --idle-warn 300
  local rc=${PIPESTATUS[0]}
  set -e
  # Do not `return $rc`: under `set -e` a non-zero return would abort the
  # script before the timeout/failure branch below can print the log.
  ATTEMPT_RC=$rc
  return 0
}

begin_stage unity-export
rm -f Temp/UnityLockfile
ATTEMPT_START=$(date +%s)
unity_attempt batch "$UNITY_TIMEOUT"
EXPORT_RC=$ATTEMPT_RC
ATTEMPT_SECONDS=$(( $(date +%s) - ATTEMPT_START ))

# A Personal licence activated from account credentials does not always carry
# the `com.unity.editor.headless` entitlement that Unity 6 asks for in batch
# mode. The retry runs the editor on the virtual display without -batchmode;
# it is bounded to 25m and only happens after a fast, recognisable failure, so
# it cannot turn a hang into another multi-hour run.
if [ "$EXPORT_RC" -ne 0 ] && [ "$ATTEMPT_SECONDS" -lt 420 ] \
   && [ "$UNITY_MODE" != "batch" ] && grep -q "com.unity.editor.headless" "$UNITY_LOG" 2>/dev/null; then
  log "batch mode hit the headless entitlement after ${ATTEMPT_SECONDS}s; retrying once on the virtual display without -batchmode"
  ATTEMPT_START=$(date +%s)
  unity_attempt gui 25m
  EXPORT_RC=$ATTEMPT_RC
  ATTEMPT_SECONDS=$(( $(date +%s) - ATTEMPT_START ))
fi

if [ "$EXPORT_RC" -ne 0 ]; then
  LAST_STAGE=$(awk -F'\t' 'NF==2{stage=$2} END{print stage}' "$STAGE_LOG" 2>/dev/null || true)
  LAST_STAGE="${LAST_STAGE:-unknown}"
  if [ "$EXPORT_RC" -eq 124 ] || [ "$EXPORT_RC" -eq 137 ]; then
    log "::error::Unity was killed by the ${UNITY_TIMEOUT} timeout after ${ATTEMPT_SECONDS}s, last stage 'unity-export'; the log below names the last line"
  else
    log "::error::Unity export failed with exit $EXPORT_RC after ${ATTEMPT_SECONDS}s"
  fi
  {
    echo "stage: unity-export"
    echo "exit_code: $EXPORT_RC"
    echo "attempt_seconds: $ATTEMPT_SECONDS"
    echo "log: $UNITY_LOG"
    echo "last_lines:"
    tail -5 "$UNITY_LOG" 2>/dev/null || true
  } > Build/unity-failure.txt
  echo "=== last 120 lines of $UNITY_LOG (full log: $(wc -l < "$UNITY_LOG" 2>/dev/null || echo 0) lines) ==="
  tail -120 "$UNITY_LOG" 2>/dev/null || true
  echo "=== end of $UNITY_LOG ==="
  exit 1
fi

if [ ! -f "$APK" ]; then
  log "::error::the Unity build finished without producing $APK"
  tail -120 "$UNITY_LOG" 2>/dev/null || true
  exit 1
fi
end_stage

begin_stage verify-apk
log "build time: $(( $(date +%s) - JOB_START )) s"
log "APK: $APK"
wc -c "$APK" | awk '{printf "APK size: %.0f bytes (%.1f MiB)\n", $1, $1/1048576}'
sha256sum "$APK"
log "ABIs inside the APK:"
unzip -l "$APK" | grep "lib/" | awk '{print $4}' | sort -u || true
end_stage

log "done"
