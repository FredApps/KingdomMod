#!/usr/bin/env python3
"""Local web UI for designing KingdomMod custom mount assets."""

from __future__ import annotations

import base64
import io
import json
import mimetypes
import os
import subprocess
import sys
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

from challenge_designer import default_design as default_challenge_design
from challenge_designer import export_design as export_challenge_design
from challenge_designer import load_templates as load_challenge_templates
from challenge_designer import normalize_design as normalize_challenge_design
from renderer import DEFAULT_DESIGN, DEFAULT_EXPORT, ROOT, export_frames, export_gif, export_sheet, load_design, save_design, seed_workspace


WORKSPACE = ROOT / "build" / "asset-designer"
STATIC = Path(__file__).resolve().parent / "static"


def find_game_dir() -> Path | None:
    override = os.environ.get("KINGDOMMOD_GAME_DIR")
    if override:
        path = Path(override)
        if (path / "KingdomTwoCrowns.exe").exists() and (path / "KingdomTwoCrowns_Data").exists():
            return path
    candidates = []
    steam = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Steam"
    candidates.append(steam / "steamapps" / "common" / "Kingdom Two Crowns")
    candidates.append(Path(r"C:\GOG Games\Kingdom Two Crowns"))
    candidates.append(Path(r"C:\Program Files\Epic Games\KingdomTwoCrowns"))
    for candidate in candidates:
        if (candidate / "KingdomTwoCrowns.exe").exists() and (candidate / "KingdomTwoCrowns_Data").exists():
            return candidate
    return None


def json_response(handler: BaseHTTPRequestHandler, payload: object, code: int = 200) -> None:
    raw = json.dumps(payload).encode("utf-8")
    handler.send_response(code)
    handler.send_header("Content-Type", "application/json; charset=utf-8")
    handler.send_header("Content-Length", str(len(raw)))
    handler.end_headers()
    handler.wfile.write(raw)


def read_json(handler: BaseHTTPRequestHandler) -> dict:
    length = int(handler.headers.get("Content-Length", "0"))
    if length <= 0:
        return {}
    return json.loads(handler.rfile.read(length).decode("utf-8"))


def image_data_url(img) -> str:
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def safe_workspace_path(rel: str) -> Path:
    rel = unquote(rel).replace("\\", "/").lstrip("/")
    path = (WORKSPACE / rel).resolve()
    if WORKSPACE.resolve() not in path.parents and path != WORKSPACE.resolve():
        raise ValueError("path escapes workspace")
    return path


class DesignerHandler(BaseHTTPRequestHandler):
    server_version = "KingdomModAssetDesigner/0.1"

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/api/design":
            design = load_design(DEFAULT_DESIGN if DEFAULT_DESIGN.exists() else None)
            json_response(self, {"design": design, "defaultDesign": str(DEFAULT_DESIGN), "defaultExport": str(DEFAULT_EXPORT)})
            return
        if parsed.path == "/api/references":
            json_response(self, {"references": self.references()})
            return
        if parsed.path == "/api/extraction-status":
            status_path = WORKSPACE / "extraction-status.json"
            payload = json.loads(status_path.read_text(encoding="utf-8")) if status_path.exists() else {"ok": False, "message": "Extraction has not run yet."}
            json_response(self, payload)
            return
        if parsed.path == "/api/challenge/templates":
            json_response(self, load_challenge_templates())
            return
        if parsed.path == "/api/challenge/design":
            json_response(self, {"design": default_challenge_design()})
            return
        if parsed.path.startswith("/workspace/"):
            path = safe_workspace_path(parsed.path[len("/workspace/"):])
            if path.exists() and path.is_file():
                self.serve_file(path)
            else:
                self.send_error(404)
            return
        path = STATIC / (parsed.path.lstrip("/") or "index.html")
        if path.is_dir():
            path = path / "index.html"
        if path.exists():
            self.serve_file(path)
        else:
            self.send_error(404)

    def do_POST(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/api/preview":
            payload = read_json(self)
            design = payload.get("design") or load_design(DEFAULT_DESIGN if DEFAULT_DESIGN.exists() else None)
            group = payload.get("group") or next(iter(design.get("animations", {"idle": 1}).keys()))
            count = int(design.get("animations", {}).get(group, 1))
            from renderer import draw_frame
            frames = [image_data_url(draw_frame(design, group, i, count)) for i in range(count)]
            json_response(self, {"group": group, "frames": frames})
            return
        if parsed.path == "/api/export":
            payload = read_json(self)
            design = payload.get("design") or load_design(DEFAULT_DESIGN if DEFAULT_DESIGN.exists() else None)
            out = ROOT / (design.get("export", {}).get("frames") or str(DEFAULT_EXPORT.relative_to(ROOT)))
            count = export_frames(design, out)
            exported_design = WORKSPACE / "exports" / "latest.mount-design.json"
            save_design(exported_design, design)
            sheet = WORKSPACE / "exports" / "latest-sheet.png"
            export_sheet(design, sheet)
            gif = WORKSPACE / "exports" / "latest-preview.gif"
            export_gif(design, gif)
            json_response(self, {
                "ok": True,
                "frames": count,
                "out": str(out),
                "sheet": "/workspace/exports/latest-sheet.png",
                "gif": "/workspace/exports/latest-preview.gif",
                "design": "/workspace/exports/latest.mount-design.json",
            })
            return
        if parsed.path == "/api/save-design":
            payload = read_json(self)
            design = payload.get("design") or load_design(DEFAULT_DESIGN if DEFAULT_DESIGN.exists() else None)
            target = WORKSPACE / "designs" / (payload.get("name") or "working.mount-design.json")
            save_design(target, design)
            json_response(self, {"ok": True, "path": str(target)})
            return
        if parsed.path == "/api/extract":
            game_dir = find_game_dir()
            if game_dir is None:
                json_response(self, {"ok": False, "message": "Kingdom Two Crowns was not auto-detected."}, 200)
                return
            script = Path(__file__).resolve().parent / "extractor.py"
            proc = subprocess.run([sys.executable, str(script), "--game-dir", str(game_dir), "--workspace", str(WORKSPACE)], capture_output=True, text=True)
            try:
                payload = json.loads(proc.stdout)
            except Exception:
                payload = {"ok": False, "message": proc.stderr or proc.stdout or "Extraction failed."}
            json_response(self, payload)
            return
        if parsed.path == "/api/challenge/preview":
            payload = read_json(self)
            design = normalize_challenge_design(payload.get("design") or default_challenge_design())
            json_response(self, {
                "ok": True,
                "name": design["name"],
                "summary": f"{design['name']}: {len(design['islands'])} island(s), base challenge {design.get('baseChallenge') or design.get('baseChallengeType') or 'auto'}",
                "design": design,
            })
            return
        if parsed.path == "/api/challenge/export":
            payload = read_json(self)
            design = payload.get("design") or default_challenge_design()
            result = export_challenge_design(design, find_game_dir())
            json_response(self, result)
            return
        self.send_error(404)

    def references(self) -> list[dict]:
        refs = []
        for root in (WORKSPACE / "references").glob("*"):
            if not root.is_dir():
                continue
            for path in sorted(root.rglob("*.png"))[:300]:
                refs.append({
                    "name": path.stem,
                    "kind": root.name,
                    "url": "/workspace/" + path.relative_to(WORKSPACE).as_posix(),
                })
        return refs

    def serve_file(self, path: Path) -> None:
        raw = path.read_bytes()
        ctype = mimetypes.guess_type(str(path))[0] or "application/octet-stream"
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def log_message(self, fmt: str, *args) -> None:
        print("[asset-designer] " + fmt % args)


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description="Start the KingdomMod asset designer.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8787)
    parser.add_argument("--game-dir")
    parser.add_argument("--no-open", action="store_true")
    parser.add_argument("--extract", action="store_true", help="run first-run extraction before serving")
    args = parser.parse_args()

    seed_workspace(WORKSPACE)
    if args.game_dir:
        os.environ["KINGDOMMOD_GAME_DIR"] = args.game_dir
    if args.extract:
        game_dir = find_game_dir()
        if game_dir:
            subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "extractor.py"), "--game-dir", str(game_dir), "--workspace", str(WORKSPACE)])

    server = ThreadingHTTPServer((args.host, args.port), DesignerHandler)
    url = f"http://{args.host}:{args.port}/"
    print(f"KingdomMod Asset Designer: {url}")
    print(f"Workspace: {WORKSPACE}")
    if not args.no_open:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
