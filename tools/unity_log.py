#!/usr/bin/env python3
"""Stream a Unity editor log to stdout with elapsed time and stage markers.

Unity is invoked with `-logFile -` so the editor log is the CI log. This filter
sits in the pipe and:

* prefixes every line with the elapsed time (`[+ 12:34]`),
* keeps a complete copy in a file for the workflow artifact,
* prints a banner the first time a build stage is reached, so the CI output
  names the stage instead of going silent,
* prints a heartbeat when Unity stops writing for a while.

The missing heartbeat is what made the 5h32m hang undiagnosable: the editor was
inside `BuildPipeline.BuildPlayer` with its log redirected to a file nobody
could read until the job was cancelled.

Standard library only, no dependency to install.
"""
from __future__ import annotations

import argparse
import os
import re
import select
import sys
import time

# Ordered stages: the first pattern that matches moves the log forward. A stage
# never moves backwards, so a word like "Gradle" in a licence line cannot
# rewind the report.
STAGES = (
    ("licence", r"\[Licensing::|Licensing::Module|LicenseClient"),
    ("project-import", r"Start importing|Refreshing native plugins|Asset Pipeline Refresh"),
    ("shader-compile", r"Compiling shader|Shader compilation|Compiling [0-9]+ shader"),
    ("il2cpp", r"il2cpp|IL2CPP|UnusedByteCodeStripper|Unity\.ILPP"),
    ("gradle", r"[Gg]radle|Building Android|Building Player|\.apk"),
    ("finish", r"Build Finished|Exiting|Application is shutting down"),
)

COMPILED = tuple((name, re.compile(pattern)) for name, pattern in STAGES)

FATAL = re.compile(
    r"Error building player|Build Finished, Result: Failure|"
    r"No valid Unity Editor license|License is not active|"
    r"Aborting batchmode due to failure",
    re.IGNORECASE,
)


def stamp(seconds: float) -> str:
    total = int(seconds)
    return f"[+{total // 60:02d}:{total % 60:02d}]"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", required=True, help="file that receives the raw log")
    parser.add_argument("--stage-log", default="", help="file that receives `elapsed<TAB>stage`")
    parser.add_argument("--idle-warn", type=int, default=300,
                        help="seconds without output before a heartbeat (0 disables)")
    args = parser.parse_args()

    parent = os.path.dirname(os.path.abspath(args.log))
    if parent:
        os.makedirs(parent, exist_ok=True)

    started = time.monotonic()
    stage_index = -1
    stage_name = "startup"
    idle = 0.0
    written = 0
    failure_seen = False

    with open(args.log, "w", encoding="utf-8", errors="replace") as log_file:
        def emit(line: str) -> None:
            sys.stdout.write(line if line.endswith("\n") else line + "\n")
            sys.stdout.flush()

        def banner(name: str) -> None:
            nonlocal stage_index, stage_name
            stage_index += 1
            stage_name = name
            emit(f"===== stage: {name} at {stamp(time.monotonic() - started)} =====")
            if args.stage_log:
                with open(args.stage_log, "a", encoding="utf-8") as handle:
                    handle.write(f"{int(time.monotonic() - started)}\t{name}\n")

        emit(f"===== unity log -> {args.log} =====")
        fd = sys.stdin.fileno()
        buffer = b""
        while True:
            ready, _, _ = select.select([fd], [], [], 1.0)
            if not ready:
                idle += 1.0
                if args.idle_warn and idle >= args.idle_warn:
                    emit(f"{stamp(time.monotonic() - started)} (no Unity output for "
                         f"{int(idle)}s, still in stage '{stage_name}')")
                    idle = 0.0
                continue
            chunk = os.read(fd, 65536)
            if not chunk:
                break
            idle = 0.0
            buffer += chunk
            while b"\n" in buffer:
                raw, buffer = buffer.split(b"\n", 1)
                line = raw.decode("utf-8", "replace").rstrip("\r")
                log_file.write(line + "\n")
                written += 1
                if written % 200 == 0:
                    log_file.flush()
                emit(f"{stamp(time.monotonic() - started)} {line}")
                for index, (name, pattern) in enumerate(COMPILED):
                    if index > stage_index and pattern.search(line):
                        banner(name)
                        break
                if FATAL.search(line) and not failure_seen:
                    failure_seen = True
                    emit(f"===== unity reported a fatal line at {stamp(time.monotonic() - started)} =====")
        if buffer:
            log_file.write(buffer.decode("utf-8", "replace") + "\n")
        log_file.flush()

    emit(f"===== unity log ended after {int(time.monotonic() - started)}s "
         f"({written} lines, last stage '{stage_name}') =====")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
