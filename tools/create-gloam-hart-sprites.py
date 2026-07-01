#!/usr/bin/env python3
"""Regenerate Gloam Hart sprites through the asset-designer renderer."""

from __future__ import annotations

import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DESIGNER = ROOT / "tools" / "asset-designer"
sys.path.insert(0, str(DESIGNER))

from renderer import DEFAULT_DESIGN, DEFAULT_EXPORT, export_frames, export_sheet, load_design  # noqa: E402


def main() -> None:
    design = load_design(DEFAULT_DESIGN)
    total = export_frames(design, DEFAULT_EXPORT)
    export_sheet(design, ROOT / "build" / "asset-designer" / "exports" / "gloam-hart-sheet.png")
    print(f"Wrote {total} Gloam Hart frames to {DEFAULT_EXPORT}")


if __name__ == "__main__":
    main()
