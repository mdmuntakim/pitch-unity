# Matchday Eleven

An original 11v11 football simulation for **Android arm64**, built with **Unity 6 LTS
(6000.0.58f1)** and **URP** (Forward+, pinned 17.0.4).

eFootball-style on-screen controls, full match rules (set pieces, fouls, cards,
offside, substitutions), team AI, procedural stadium — every source file and every
texture in the game is **original**, generated or written in this repository. Nothing
is ripped or licensed from any existing game.

**One command build:** `bash build/export.sh` → `Build/MatchdayEleven-arm64.apk`

## Controls (touch)

| Input | Action |
| --- | --- |
| Left thumb | Floating virtual joystick (appears where you touch, drags with you) |
| SHOOT | Hold to charge (power bar + ring arc), release to shoot |
| PASS | Short pass, stick-direction bias |
| THRU | Through ball into space |
| LOB | Lofted pass / cross into the box |
| SPR | Sprint (hold); pushing the stick to the edge also sprints |
| TACKLE / PRESSURE | Contextual: slide tackle when defending, press when loose |

Multi-touch is fully independent per finger (joystick + buttons simultaneously),
actions fire from the control model (no polling latency), haptic feedback on button
press. Button size, opacity, left/right-handed layout and haptics are adjustable in
the menu and persist in `PlayerPrefs`.

Desktop testing: the engine core is covered by the headless C# tests, and the input
mapping is exercised through `ControlModel` / `Pitch.Core.Tests`.

## Gameplay

- 11 v 11, two halves of configurable length, half-time and full-time states.
- Formations 4-4-2 / 4-3-3 / 3-5-2 with role-based anchors, stamina, pressing,
  marking, passing-lane evaluation and off-ball runs.
- Ball: gravity, drag, Magnus spin curve, bounce restitution, rolling friction,
  post/crossbar deflection.
- Set pieces: kick-off, goal kick, throw-in, corner, free kick, penalty.
- Referee: tackle foul decisions, yellow/red cards, offside approximation.
- Goalkeeper AI: positioning on the ball-goal line, dives, claims, distribution.

## Visual / presentation

- Broadcast look: ACES-style colour pipeline through URP Forward+, soft directional
  sun shadows, ambient occlusion, bloom, TAA, motion blur, depth of field on cinematics.
- Procedurally generated PBR stadium (grass with mowing stripes and goal wear, stands,
  floodlights), animated crowd, scoreboard, lower thirds, tifo, goal celebrations, replays.
- The **blue possession ring** is rendered under the ball carrier of the **user's own
  team only**, and the **match ball** gets a rim light + motion trail highlight.
- Pre-kick-off World Cup-style trophy walk-past cinematic (tunnel/centre circle, teams in
  line, crowd roar, broadcast graphics) driven by `IntroDirector` + `TrophyIntroView`.

## Performance

- 60 fps target at 1080p on a mid-range arm64 phone, 30 fps hard floor, frame budget
  of 16.6 ms (60 Hz) with dynamic resolution scaling and render interpolation between
  fixed physics steps.
- Budget: URP batcher, GPU instancing, occlusion culling, LOD groups, texture streaming,
  object pooling, no per-frame allocations in the core.

## Engine-side tests

```
dotnet run --project tests/CoreTests
```

136 checks: ball physics (settle, friction, bounce, curve, post deflection), match state
(halves, clock, out-of-play rules), formations (3 shapes, mirroring), referee (fouls,
cards), offside line, input mapping (joystick, multi-touch, settings), quality model,
replay, intro, full match flow (kick-off → play → throw-in → goal → celebration → half
time → full time), substitutions and determinism (same seed, same result).

## Build

| Pin | Value |
| --- | --- |
| Unity editor | 6000.0.58f1 (LTS, Linux x86_64) |
| URP package | 17.0.4 (Forward+) |
| Scripting backend | IL2CPP (managed stripping: High) |
| Texture compression | ASTC 6x6 |
| ABI | `arm64-v8a` only |
| Signing | `signing/debug.keystore` (public project key, user `androiddebugkey`, password `android`) |

`bash build/export.sh` downloads the pinned engine (cached), generates the deterministic
PBR texture set when absent (`tools/asset_generator.py`), runs the headless engine tests
and exports the signed APK through the editor batch build
(`Pitch.EditorTools.BuildAndroid`). The build writes `Build/build-facts.txt` and
`Build/report.json` (bytes, SHA-256, ABIs, size band, build seconds).

CI (`.github/workflows/build.yml`) runs the same pipeline on `ubuntu-latest` with the
Unity editor and import cache checkpointed between runs, verifies that the APK is
`arm64-v8a` only and inside the 200–300 MB band, and uploads it as a workflow artifact.
A **release** (APK + `SHA256SUMS.txt`, stage timings, screenshots) is published only by a
manual run with `publish_release=true`; a push to `main` never publishes. A push and a
dispatch for the same commit share a concurrency group, so one commit is never built
twice.

The job needs `UNITY_LICENSE` (contents of a `.ulf` licence file, raw or base64) or
`UNITY_EMAIL` + `UNITY_PASSWORD`. If any of them is missing the job stops before
downloading the editor and names the missing secrets; it never uploads a placeholder APK.

The licence itself must grant `com.unity.editor.headless`: Unity 6 asks for that
entitlement for every `-batchmode` player build, and an account-only Personal activation
that returns `Found 0 entitlement groups` / `'com.unity.editor.headless' was not found`
cannot build an APK at all (the editor imports the project, then
`BuildPipeline.IsBuildTargetSupported` is false). A serial-activated licence
(`UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD`) or a matching `.ulf` in
`UNITY_LICENSE` is required. The preflight prints this diagnosis, and the licence
artifacts Unity wrote, in about a minute; it does not start the long build.

### A hang cannot burn hours

The job is bounded at 120 minutes, not at GitHub's 6 hour kill, and every step below is
timed:

| Stage | Bound | Where it shows |
| --- | --- | --- |
| whole job | `timeout-minutes: 120` | workflow |
| `build/export.sh` | `timeout --signal=INT 75m` | workflow step |
| one Unity run | `timeout --signal=INT 60m` (25m for the GUI retry) | `build/export.sh` |
| licence + Android target preflight | 15m, fails in ~1m | `build/export.sh` |
| screenshot capture (release only) | 15m, `continue-on-error` | workflow |

`bash build/export.sh` prints a `--- stage: name ---` banner and a `[+mm:ss]` prefix on
every line, times `environment`, `textures`, `engine-tests`, `unity-preflight`,
`unity-export` and `verify-apk` in `Build/stage-timings.txt`, and runs the editor with
`-logFile -` through `tools/unity_log.py`, which adds the elapsed time, names the Unity
stage (import, shader compile, IL2CPP, Gradle) and prints a heartbeat when the editor
stops writing. On timeout it prints the last 120 lines of the log and writes
`Build/unity-failure.txt`; the workflow always uploads `Build/logs/` (Unity log,
preflight log, screenshot log) and the stage timings as the `unity-editor-log` artifact,
on success, failure and cancellation alike.

Unity 6 refuses a player build in `-batchmode` when the licence lacks the
`com.unity.editor.headless` entitlement ("Error building player because build target was
unsupported"). The preflight reports `[Pitch] PROBE android_supported=...` in both modes
in about ten seconds, and `export.sh` picks the mode from that answer: batch mode when it
is usable, the interactive editor on Xvfb otherwise. Both are under the same timeouts and
the same streamed log, so neither can wait forever.

### Size budget

The ~250 MB APK band (200–300 MB) is enforced by the build (`PITCH_ENFORCE_SIZE=1` fails
outside the band); the target is set by the generated texture set, audio compression and
the stripped IL2CPP engine.

## Project structure

```
Assets/Scripts/Core/   engine independent C# core (no UnityEngine reference)
Assets/Scripts/Game/   Unity presentation layer (stadium, broadcast camera, HUD,
                       touch controls, audio, quality, replay, screenshots, intro)
Assets/Scripts/Editor/ Unity editor tooling (batch build, project bootstrap, URP settings)
Assets/Shaders/         procedural URP shaders (possession ring, ball highlight, grass, net, crowd)
Assets/Tests/EditMode/ Unity edit-mode tests
Assets/Scenes/          boot scene (created by Pitch.EditorTools.ProjectBootstrap when absent)
Assets/Settings/        URP assets (created by ProjectBootstrap when absent)
Assets/Resources/       generated textures (created by tools/asset_generator.py)
Assets/Generated/       generated asset placeholders
tests/CoreTests/        headless C# engine tests (dotnet)
tools/                   asset generator (Python), deterministic textures
build/                   export script (one command build)
signing/                 release keystore
docs/                    screenshots and notes
```

## License / provenance

All code and generated assets are original, produced in this repository. No club names,
badges, kits, player likenesses or extracted models from any existing game.
"Matchday Eleven" is not an existing franchise name.
