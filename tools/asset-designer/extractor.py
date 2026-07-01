#!/usr/bin/env python3
"""Private local reference extraction for the KingdomMod asset designer."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any


TEXTURE_KEYWORDS = (
    "steed",
    "stag",
    "reindeer",
    "horse",
    "mount",
    "wolf",
    "bear",
    "lizard",
    "griffin",
    "unicorn",
)


def extract_references(game_dir: Path, workspace: Path, limit: int = 200) -> dict[str, Any]:
    out_dir = workspace / "references" / "game"
    out_dir.mkdir(parents=True, exist_ok=True)
    status: dict[str, Any] = {
        "ok": False,
        "exported": 0,
        "directory": str(out_dir),
        "message": "",
        "requires": "UnityPy",
    }

    data_dir = game_dir / "KingdomTwoCrowns_Data"
    if not data_dir.exists():
        status["message"] = f"Game data folder not found: {data_dir}"
        write_status(workspace, status)
        return status

    try:
        import UnityPy  # type: ignore
    except Exception as exc:
        status["message"] = f"UnityPy is not installed in this Python environment: {exc}"
        write_status(workspace, status)
        return status

    asset_files = sorted(
        list(data_dir.glob("resources.assets"))
        + list(data_dir.glob("sharedassets*.assets"))
        + list(data_dir.glob("level*"))
    )
    if not asset_files:
        status["message"] = f"No Unity asset files found under {data_dir}"
        write_status(workspace, status)
        return status

    exported = 0
    keyword_matches = 0
    metadata: list[dict[str, Any]] = []
    for keyword_only in (True, False):
        if exported >= limit or (not keyword_only and keyword_matches > 0):
            break
        for asset_file in asset_files:
            if exported >= limit:
                break
            try:
                env = UnityPy.load(str(asset_file))
            except Exception:
                continue
            for obj in env.objects:
                if exported >= limit:
                    break
                if obj.type.name not in ("Texture2D", "Sprite"):
                    continue
                try:
                    data = obj.read()
                    name = getattr(data, "name", "") or f"{obj.type.name}_{obj.path_id}"
                    haystack = name.lower()
                    is_keyword = any(k in haystack for k in TEXTURE_KEYWORDS)
                    if keyword_only and not is_keyword:
                        continue
                    image = data.image
                    if image is None or image.width < 8 or image.height < 8:
                        continue
                    if is_keyword:
                        keyword_matches += 1
                    safe = "".join(c if c.isalnum() or c in "-_." else "_" for c in name)[:100]
                    rel = f"{exported:04d}_{safe}.png"
                    image.save(out_dir / rel)
                    metadata.append({
                        "name": name,
                        "type": obj.type.name,
                        "source": str(asset_file),
                        "pathId": int(obj.path_id),
                        "file": rel,
                        "width": image.width,
                        "height": image.height,
                        "keywordMatch": is_keyword,
                    })
                    exported += 1
                except Exception:
                    continue

    (out_dir / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    status["ok"] = exported > 0
    status["exported"] = exported
    status["message"] = (
        f"Exported {exported} private game reference image(s)."
        if exported
        else "No mount-like Texture2D/Sprite references were found; generated examples are still available."
    )
    write_status(workspace, status)
    return status


def write_status(workspace: Path, status: dict[str, Any]) -> None:
    workspace.mkdir(parents=True, exist_ok=True)
    (workspace / "extraction-status.json").write_text(json.dumps(status, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="Extract private game references for the asset designer.")
    parser.add_argument("--game-dir", required=True, type=Path)
    parser.add_argument("--workspace", required=True, type=Path)
    parser.add_argument("--limit", type=int, default=200)
    args = parser.parse_args()
    print(json.dumps(extract_references(args.game_dir, args.workspace, args.limit), indent=2))
