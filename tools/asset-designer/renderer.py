#!/usr/bin/env python3
"""Design-driven sprite renderer for KingdomMod custom mount assets."""

from __future__ import annotations

import json
import math
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DESIGN = ROOT / "examples" / "GloamHart" / "design" / "gloam_hart.mount-design.json"
DEFAULT_EXPORT = ROOT / "examples" / "GloamHart" / "assets" / "gloam_hart"


def px(value: float) -> int:
    return int(round(value))


def rgba(value: Any, fallback: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    if isinstance(value, str):
        s = value.strip().lstrip("#")
        if len(s) == 6:
            return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), 255)
        if len(s) == 8:
            return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), int(s[6:8], 16))
    if isinstance(value, list) and len(value) in (3, 4):
        vals = [max(0, min(255, int(v))) for v in value]
        if len(vals) == 3:
            vals.append(255)
        return tuple(vals)  # type: ignore[return-value]
    return fallback


def deep_merge(base: dict[str, Any], override: dict[str, Any]) -> dict[str, Any]:
    out = dict(base)
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(out.get(key), dict):
            out[key] = deep_merge(out[key], value)
        else:
            out[key] = value
    return out


@dataclass(frozen=True)
class RenderedFrame:
    group: str
    index: int
    image: Image.Image


def default_design() -> dict[str, Any]:
    return {
        "schema": 1,
        "name": "Gloam Hart",
        "description": "A luminous forest hart with mothlike antlers.",
        "frame": {"width": 96, "height": 64, "pixelsPerUnit": 16, "pivot": [0.5, 0.2]},
        "animations": {"idle": 6, "walk": 8, "run": 8, "eat": 4, "rear": 3, "tired": 3},
        "palette": {
            "outline": "#141926ff",
            "shadow": "#242e3aff",
            "coat": "#3e535fff",
            "highlight": "#7daeb2ff",
            "chest": "#31414dff",
            "glow": "#91e9e6e6",
            "antler": "#c0e7d9ff",
            "hoof": "#0e1015ff",
            "aura": "#48b2cd22",
        },
        "body": {
            "x": 54,
            "y": 35,
            "width": 36,
            "height": 17,
            "bob": 1.5,
            "headX": 77,
            "headY": 27,
            "neckRaise": 0,
            "legAmp": 3.0,
            "tailLength": 10,
            "glowMarks": [45, 53, 61],
            "antlerScale": 1.0,
        },
        "motion": {
            "walkLegAmp": 3.5,
            "runBobScale": 1.8,
            "runLegAmp": 5.0,
            "runSpeedAmp": 1.7,
            "eatHeadY": 40,
            "rearLift": 2,
            "tiredBodyY": 38,
        },
        "export": {"frames": "examples/GloamHart/assets/gloam_hart"},
    }


def load_design(path: Path | None = None) -> dict[str, Any]:
    design = default_design()
    if path and path.exists():
        design = deep_merge(design, json.loads(path.read_text(encoding="utf-8")))
    return design


def save_design(path: Path, design: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(design, indent=2) + "\n", encoding="utf-8")


def draw_frame(design: dict[str, Any], group: str, frame: int, count: int) -> Image.Image:
    frame_cfg = design.get("frame", {})
    body_cfg = design.get("body", {})
    motion = design.get("motion", {})
    pal = design.get("palette", {})
    width = int(frame_cfg.get("width", 96))
    height = int(frame_cfg.get("height", 64))
    phase = frame / max(count, 1)

    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    glow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    d = ImageDraw.Draw(img)

    bob = math.sin(phase * math.tau) * float(body_cfg.get("bob", 1.5))
    stride = math.sin(phase * math.tau)
    speed_amp = 1.0
    body_y = float(body_cfg.get("y", 35)) + bob
    head_y = float(body_cfg.get("headY", 27)) + bob * 0.5
    neck_raise = float(body_cfg.get("neckRaise", 0))
    leg_amp = float(body_cfg.get("legAmp", 3.0))

    if group == "run":
        bob *= float(motion.get("runBobScale", 1.8))
        body_y = float(body_cfg.get("y", 35)) - 1 + bob
        head_y = float(body_cfg.get("headY", 27)) - 2 + bob * 0.4
        speed_amp = float(motion.get("runSpeedAmp", 1.7))
        leg_amp = float(motion.get("runLegAmp", 5.0))
    elif group == "walk":
        leg_amp = float(motion.get("walkLegAmp", 3.5))
    elif group == "eat":
        head_y = float(motion.get("eatHeadY", 40)) + math.sin(phase * math.tau) * 1.0
        neck_raise = 7
        leg_amp = 0.6
    elif group == "rear":
        body_y = float(body_cfg.get("y", 35)) - 3 - frame * float(motion.get("rearLift", 2))
        head_y = float(body_cfg.get("headY", 27)) - 9 - frame
        neck_raise = -8
        leg_amp = 2.0
    elif group == "tired":
        body_y = float(motion.get("tiredBodyY", 38)) + math.sin(phase * math.tau) * 0.5
        head_y = float(body_cfg.get("headY", 27)) + 7
        neck_raise = 4
        leg_amp = 0.5

    body_x = float(body_cfg.get("x", 54))
    body_w = float(body_cfg.get("width", 36))
    body_h = float(body_cfg.get("height", 17))
    body = (px(body_x - body_w / 2), px(body_y - body_h / 2), px(body_x + body_w / 2), px(body_y + body_h / 2))
    neck = [
        (px(body_x + 12), px(body_y - 5)),
        (px(float(body_cfg.get("headX", 77)) - 1), px(head_y + neck_raise)),
        (px(float(body_cfg.get("headX", 77)) - 5), px(head_y + neck_raise + 4)),
        (px(body_x + 7), px(body_y - 2)),
    ]
    head_x = float(body_cfg.get("headX", 77))
    head = (px(head_x - 7), px(head_y + neck_raise - 5), px(head_x + 7), px(head_y + neck_raise + 5))
    chest = (px(body_x + 2), px(body_y - 8), px(body_x + 19), px(body_y + 7))

    outline = rgba(pal.get("outline"), (20, 25, 38, 255))
    shadow = rgba(pal.get("shadow"), (36, 46, 58, 255))
    coat = rgba(pal.get("coat"), (62, 83, 95, 255))
    coat_hi = rgba(pal.get("highlight"), (125, 174, 178, 255))
    chest_c = rgba(pal.get("chest"), (49, 65, 77, 255))
    glow_c = rgba(pal.get("glow"), (145, 233, 230, 230))
    antler = rgba(pal.get("antler"), (192, 231, 217, 255))
    hoof = rgba(pal.get("hoof"), (14, 16, 21, 255))
    aura = rgba(pal.get("aura"), (72, 178, 205, 34))

    g.ellipse((px(body_x - 30), px(body_y - 18), px(body_x + 34), px(body_y + 17)), fill=aura)
    glow = glow.filter(ImageFilter.GaussianBlur(5))
    img.alpha_composite(glow)

    d.ellipse((px(body_x - 28), height - 13, px(body_x + 23), height - 8), fill=(0, 0, 0, 48))
    d.ellipse(body, fill=outline)
    d.ellipse((body[0] + 2, body[1] + 2, body[2] - 2, body[3] - 2), fill=coat)
    d.ellipse((body[0] + 7, body[1] + 3, body[2] - 6, body[1] + 9), fill=coat_hi)
    d.polygon(neck, fill=outline)
    d.polygon([(x + (1 if i % 2 else 0), y + 1) for i, (x, y) in enumerate(neck)], fill=coat)
    d.ellipse(chest, fill=chest_c)
    d.ellipse(head, fill=outline)
    d.ellipse((head[0] + 1, head[1] + 1, head[2] - 1, head[3] - 1), fill=(70, 91, 101, 255))
    d.rectangle((head[2] - 2, head[1] + 2, head[2] + 4, head[1] + 6), fill=outline)
    d.rectangle((head[2] - 1, head[1] + 3, head[2] + 5, head[1] + 5), fill=(68, 89, 99, 255))
    d.point((head[2] - 4, head[1] + 2), fill=glow_c)

    antler_base_x, antler_base_y = px(head_x - 2), head[1]
    antler_scale = float(body_cfg.get("antlerScale", 1.0))
    for side in (-1, 1):
        root = (antler_base_x, antler_base_y + 1)
        tip = (px(antler_base_x + side * (5 + frame % 2) * antler_scale), px(antler_base_y - 9 * antler_scale))
        d.line((root, tip), fill=antler, width=2)
        d.line((tip, (px(tip[0] + side * 5 * antler_scale), tip[1] - 2)), fill=antler, width=1)
        d.line(((px(tip[0] - side), tip[1] + 3), (px(tip[0] + side * 6 * antler_scale), tip[1] + 4)), fill=antler, width=1)
        d.ellipse((tip[0] + side * 3 - 2, tip[1] - 4, tip[0] + side * 3 + 5, tip[1] + 4), fill=(112, 207, 202, 88), outline=antler)

    hip_points = [
        (body_x - 12, body_y + 5),
        (body_x - 4, body_y + 6),
        (body_x + 8, body_y + 6),
        (body_x + 15, body_y + 4),
    ]
    for idx, (hx, hy) in enumerate(hip_points):
        s = math.sin(phase * math.tau + idx * math.pi / 1.8) * leg_amp * speed_amp
        knee = (hx + s * 0.5, hy + 8)
        foot = (hx + s, height - 12)
        if group == "rear" and idx >= 2:
            foot = (hx + 8 + idx, height - 19 + idx)
            knee = (hx + 5, hy + 5)
        d.line((px(hx), px(hy), px(knee[0]), px(knee[1]), px(foot[0]), px(foot[1])), fill=outline, width=3)
        d.line((px(hx), px(hy), px(knee[0]), px(knee[1]), px(foot[0]), px(foot[1])), fill=shadow, width=1)
        d.rectangle((px(foot[0] - 2), px(foot[1]), px(foot[0] + 2), px(foot[1] + 2)), fill=hoof)

    tail_y = body_y - 2
    tail_len = float(body_cfg.get("tailLength", 10))
    d.line((px(body_x - 19), px(tail_y), px(body_x - 19 - tail_len), px(tail_y - 5 + stride * 2)), fill=outline, width=3)
    d.line((px(body_x - 19), px(tail_y), px(body_x - 19 - tail_len), px(tail_y - 5 + stride * 2)), fill=coat_hi, width=1)
    for x in body_cfg.get("glowMarks", [45, 53, 61]):
        y = px(body_y - 3 + math.sin(phase * math.tau + float(x)) * 2)
        d.point((px(x), y), fill=glow_c)
        d.point((px(x) + 1, y), fill=glow_c)

    return img


def render_design(design: dict[str, Any]) -> list[RenderedFrame]:
    frames: list[RenderedFrame] = []
    for group, count in design.get("animations", {}).items():
        for i in range(int(count)):
            frames.append(RenderedFrame(group, i, draw_frame(design, group, i, int(count))))
    return frames


def export_frames(design: dict[str, Any], out_dir: Path | None = None, clean: bool = True) -> int:
    if out_dir is None:
        export = design.get("export", {}).get("frames") or str(DEFAULT_EXPORT.relative_to(ROOT))
        out_dir = ROOT / export
    if clean and out_dir.exists():
        for old in out_dir.glob("*.png"):
            old.unlink()
    out_dir.mkdir(parents=True, exist_ok=True)
    count = 0
    for frame in render_design(design):
        frame.image.save(out_dir / f"{frame.group}_{frame.index}.png")
        count += 1
    return count


def export_sheet(design: dict[str, Any], path: Path) -> None:
    frames = render_design(design)
    if not frames:
        raise ValueError("design has no frames")
    w, h = frames[0].image.size
    cols = max([int(v) for v in design.get("animations", {}).values()] or [1])
    rows = len(design.get("animations", {}))
    sheet = Image.new("RGBA", (cols * w, rows * h), (0, 0, 0, 0))
    row_by_group = {group: idx for idx, group in enumerate(design.get("animations", {}).keys())}
    for frame in frames:
        sheet.alpha_composite(frame.image, (frame.index * w, row_by_group[frame.group] * h))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def export_gif(design: dict[str, Any], path: Path) -> None:
    frames = [f.image for f in render_design(design)]
    if not frames:
        raise ValueError("design has no frames")
    path.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=110, loop=0, disposal=2)


def seed_workspace(workspace: Path) -> None:
    workspace.mkdir(parents=True, exist_ok=True)
    designs = workspace / "designs"
    designs.mkdir(exist_ok=True)
    if DEFAULT_DESIGN.exists():
        target = designs / DEFAULT_DESIGN.name
        if not target.exists():
            shutil.copy2(DEFAULT_DESIGN, target)
    examples = workspace / "references" / "generated" / "gloam_hart"
    if not examples.exists():
        export_frames(load_design(DEFAULT_DESIGN if DEFAULT_DESIGN.exists() else None), examples)


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="Render a KingdomMod mount design.")
    parser.add_argument("--design", type=Path, default=DEFAULT_DESIGN)
    parser.add_argument("--out", type=Path, default=DEFAULT_EXPORT)
    parser.add_argument("--sheet", type=Path)
    args = parser.parse_args()
    design_data = load_design(args.design)
    total = export_frames(design_data, args.out)
    if args.sheet:
        export_sheet(design_data, args.sheet)
    print(f"Wrote {total} frames to {args.out}")
