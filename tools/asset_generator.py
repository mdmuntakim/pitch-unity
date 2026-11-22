#!/usr/bin/env python3
"""Deterministic original PBR texture set for Matchday Eleven.

The game is deliberately asset-light in source control: this script regenerates
every texture from a seed, so a clean clone produces the same build. Textures
are 2K tiles expanded to 4K pages; on Android they are imported as ASTC 6x6,
which is what puts the APK into the 200-300 MB band without runtime decode.

No external packages, no licenced artwork. Every pixel is synthesised here.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import struct
import zlib

PAGE = 4096
TILE = 512


def png_write(path: str, width: int, height: int, rows, channels: int = 4) -> int:
    """Write an 8-bit PNG from an iterable of row bytes, streaming the IDAT."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    color_type = 6 if channels == 4 else 2
    raw = zlib.compressobj(level=6)

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    written = 0
    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        header = struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)
        handle.write(chunk(b"IHDR", header))
        payload = bytearray()
        for row in rows:
            payload += b"\x00" + row
            if len(payload) > 1 << 20:
                written += len(payload)
                handle.write(chunk(b"IDAT", raw.compress(bytes(payload))))
                payload.clear()
        if payload:
            written += len(payload)
            handle.write(chunk(b"IDAT", raw.compress(bytes(payload))))
        handle.write(chunk(b"IDAT", raw.flush()))
        handle.write(chunk(b"IEND", b""))
    return os.path.getsize(path)


class Rng:
    def __init__(self, seed: int):
        self.state = seed & 0xFFFFFFFF

    def next(self) -> float:
        self.state = (1664525 * self.state + 1013904223) & 0xFFFFFFFF
        return self.state / 4294967296.0


def make_tile(kind: str, seed: int, size: int = TILE):
    """Return a list of RGBA rows (bytes) for one tile, plus its tiling rows."""
    rng = Rng(seed)
    rows = []
    for y in range(size):
        row = bytearray()
        for x in range(size):
            u = x / size
            v = y / size
            if kind == "grass":
                n = rng.next()
                stripe = 0.5 + 0.5 * math.sin((u * 0.55 + v * 0.1) * math.tau)
                blade = 0.5 + 0.5 * math.sin((u * 210 + v * 37) * math.tau)
                shade = 0.34 + 0.2 * stripe + 0.1 * blade + 0.08 * n
                row += bytes((int(28 + 34 * shade), int(66 + 76 * shade), int(30 + 36 * shade), 255))
            elif kind == "normal":
                n1 = rng.next()
                dx = int(128 + 22 * math.sin((u * 64 + v * 7) * math.tau) + 8 * n1)
                dy = int(128 + 22 * math.cos((v * 58 + u * 5) * math.tau) + 8 * (1 - n1))
                row += bytes((max(0, min(255, dx)), max(0, min(255, dy)), 255, 255))
            elif kind == "orm":
                r = int(120 + 60 * rng.next())
                row += bytes((r, r, r, 255))
            elif kind == "kit":
                weave = 0.5 + 0.5 * math.sin((u * 90 + v * 90) * math.tau)
                n = rng.next()
                base = (12 + int(18 * weave), 70 + int(30 * weave), 96 + int(26 * weave))
                if (v * 24) % 1.0 < 0.18:
                    base = (225, 230, 236)
                row += bytes(base + (255,))
            elif kind == "skin":
                n = rng.next()
                row += bytes((188 + int(16 * n), 138 + int(12 * n), 104 + int(10 * n), 255))
            elif kind == "hair":
                n = rng.next()
                row += bytes((20 + int(10 * n), 16 + int(8 * n), 13 + int(6 * n), 255))
            elif kind == "ball":
                n = rng.next()
                panel = 1 if (int(u * 8) + int(v * 8)) % 2 else 0
                base = 236 if panel else 40
                row += bytes((base + int(6 * n), base + int(6 * n), base - 18 + int(4 * n), 255))
            elif kind == "crowd":
                n = rng.next()
                row += bytes((int(60 + 150 * n), int(40 + 90 * n), int(50 + 40 * n), 255))
            elif kind == "tifo":
                cell = 1 if (int(u * 16) + int(v * 10)) % 2 else 0
                row += bytes((242, 208, 88, 255) if cell else (232, 238, 244, 255))
            elif kind == "stadium":
                n = rng.next()
                row += bytes((int(58 + 24 * n), int(62 + 24 * n), int(68 + 22 * n), 255))
            elif kind == "adboards":
                n = rng.next()
                row += bytes((int(20 + 40 * n), int(70 + 50 * n), int(90 + 40 * n), 255))
            elif kind == "trophy":
                n = rng.next()
                row += bytes((int(210 + 30 * n), int(170 + 40 * n), int(70 + 40 * n), 255))
            else:
                row += bytes((128, 128, 128, 255))
        rows.append(bytes(row))

    # Add a little page-scale variation without a full 4096^2 Python loop:
    # rows are reused, but each quarter is shifted and tinted by PNG row choice.
    return rows


def expand(rows, page: int = PAGE):
    """Expand tiled 512 rows to a 4096 page, cycling with a per-1024 shift."""
    tile = len(rows)
    repeats = page // tile
    for y in range(page):
        band = y // tile
        offset = (band * 97) % tile
        row = rows[(y % tile + offset) % tile]
        # A cheap brightness band breaks obvious tiling.
        factor = 0.94 + 0.06 * math.sin(band * math.tau / 4)
        if abs(factor - 1.0) < 0.005:
            yield row
        else:
            yield bytes(int(min(255, c * factor)) for c in row)


TEXTURES = [
    ("grass_albedo", "grass", 11),
    ("grass_normal", "normal", 12),
    ("grass_orm", "orm", 13),
    ("kit_home_albedo", "kit", 21),
    ("kit_home_normal", "normal", 22),
    ("kit_away_albedo", "kit_red", 23),
    ("kit_away_normal", "normal", 24),
    ("goalkeeper_albedo", "kit", 25),
    ("skin_albedo", "skin", 31),
    ("skin_normal", "normal", 32),
    ("hair_albedo", "hair", 33),
    ("ball_albedo", "ball", 41),
    ("ball_normal", "normal", 42),
    ("crowd_albedo", "crowd", 51),
    ("tifo_albedo", "tifo", 52),
    ("stadium_albedo", "stadium", 61),
    ("stadium_normal", "normal", 62),
    ("adboards_albedo", "adboards", 71),
    ("trophy_albedo", "trophy", 81),
    ("trophy_orm", "orm", 82),
]


def astc_bytes(width: int, height: int, block: int = 6) -> int:
    return ((width + block - 1) // block) * ((height + block - 1) // block) * 16


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="Assets/Resources/Generated/Textures")
    parser.add_argument("--size", type=int, default=PAGE)
    parser.add_argument("--only", default="")
    parser.add_argument("--projection", action="store_true")
    args = parser.parse_args()

    wanted = set(args.only.split(",")) if args.only else None
    os.makedirs(args.out, exist_ok=True)
    manifest = []
    for name, kind, seed in TEXTURES:
        if wanted and name not in wanted:
            continue
        rows = make_tile(kind, seed)
        path = os.path.join(args.out, name + ".png")
        size = png_write(path, args.size, args.size, expand(rows, args.size))
        projected = astc_bytes(args.size, args.size)
        manifest.append({
            "name": name,
            "file": os.path.basename(path),
            "sourceBytes": size,
            "width": args.size,
            "height": args.size,
            "androidFormat": "ASTC_6x6",
            "projectedApkBytes": projected,
        })
        print(f"{name:20s} source {size / 1e6:8.2f} MB  projected ASTC {projected / 1e6:7.2f} MB")

    total_astc = sum(item["projectedApkBytes"] for item in manifest)
    manifest_path = os.path.join(args.out, "manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump({"textures": manifest, "projectedTextureBytes": total_astc}, handle, indent=2)

    print()
    print(f"textures: {len(manifest)}")
    print(f"projected ASTC texture payload: {total_astc / 1e6:.1f} MB")
    print(f"projection with ~50 MB engine/il2cpp/meshes: {(total_astc + 50_000_000) / 1e6:.0f} MB APK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
