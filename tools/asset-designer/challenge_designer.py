#!/usr/bin/env python3
"""Challenge/island JSON helpers for the local KingdomMod asset designer."""

from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DUMP_DIR = ROOT / "docs" / "_generated" / "f3-dump" / "latest"
EXPORT_DIR = ROOT / "build" / "asset-designer" / "exports"


def _load_json(path: Path) -> list[dict]:
    if not path.exists():
        return []
    return json.loads(path.read_text(encoding="utf-8"))


def load_templates() -> dict:
    challenges = _load_json(DUMP_DIR / "challenges.json")
    levels = _load_json(DUMP_DIR / "levelconfigs.json")
    biomes = _load_json(DUMP_DIR / "biomes.json")
    usable_challenges = [
        {
            "assetName": c.get("assetName", ""),
            "id": c.get("id"),
            "challengeType": c.get("challengeType", ""),
            "isMultiplayer": c.get("isMultiplayer", True),
            "includeHermits": c.get("includeHermits", False),
            "zombieMode": c.get("zombieMode", False),
            "challengeSeed": c.get("challengeSeed", -1),
            "forceSelectBiomeIndex": c.get("forceSelectBiomeIndex", -1),
            "startingCurrencyBagType": c.get("startingCurrencyBagType", "Bag"),
            "levelConfigs": c.get("levelConfigs", []),
        }
        for c in challenges
        if c.get("challengeState") == "Available" and c.get("levelConfigs")
    ]
    usable_levels = [
        {
            "assetName": l.get("assetName", ""),
            "blockName": l.get("blockName", ""),
            "questType": l.get("questType", "None"),
            "seasonChangeDays": l.get("seasonChangeDays", 3),
            "islandMonumentID": l.get("islandMonumentID", -1),
            "startingCoins": l.get("startingCoins", 7),
            "startingBeggars": l.get("startingBeggars", 0),
            "startingPeasants": l.get("startingPeasants", 0),
            "startingCoinsContinueOverride": l.get("startingCoinsContinueOverride", -1),
            "startingGems": l.get("startingGems", 0),
            "incomeMultiplier": l.get("incomeMultiplier", 1),
            "freeBoatParts": l.get("freeBoatParts", 0),
            "caveEscapeTimer": l.get("caveEscapeTimer", 20),
            "shouldPlayVictoryMusicOnGreedDefeat": l.get("shouldPlayVictoryMusicOnGreedDefeat", True),
            "minLevelWidth": l.get("minLevelWidth", 520),
            "gemCount": l.get("gemCount", 0),
            "twoCliffs": l.get("twoCliffs", False),
            "caveless": l.get("caveless", False),
            "riverless": l.get("riverless", False),
            "randomizeCliffSide": l.get("randomizeCliffSide", False),
            "sideDistributionBias": l.get("sideDistributionBias", 5),
            "landCycleData": l.get("landCycleData"),
            "alternateLandCycleData": l.get("alternateLandCycleData"),
        }
        for l in levels
        if l.get("assetName")
    ]
    return {
        "dumpDir": str(DUMP_DIR),
        "challenges": usable_challenges,
        "levelConfigs": usable_levels,
        "biomes": biomes,
    }


def slugify(value: str) -> str:
    value = (value or "custom-challenge").strip().lower()
    value = re.sub(r"[^a-z0-9]+", "-", value).strip("-")
    return value or "custom-challenge"


def default_design() -> dict:
    templates = load_templates()
    base_challenge = _preferred(templates["challenges"], "Daily Challenge Island(Clone)") or (templates["challenges"][0] if templates["challenges"] else {})
    base_level_name = (base_challenge.get("levelConfigs") or [None])[0]
    base_level = _preferred(templates["levelConfigs"], base_level_name) or (templates["levelConfigs"][0] if templates["levelConfigs"] else {})
    name = "My Custom Island"
    return {
        "schema": 1,
        "id": f"custom.{slugify(name)}",
        "name": name,
        "description": "A custom challenge generated from dumped Kingdom Two Crowns challenge and island templates.",
        "baseChallenge": base_challenge.get("assetName", ""),
        "baseChallengeId": base_challenge.get("id"),
        "baseChallengeType": base_challenge.get("challengeType", ""),
        "baseLevelConfig": base_level.get("assetName", ""),
        "challengeSeed": 12345,
        "isMultiplayer": base_challenge.get("isMultiplayer", True),
        "includeHermits": base_challenge.get("includeHermits", True),
        "zombieMode": base_challenge.get("zombieMode", False),
        "forceSelectBiomeIndex": base_challenge.get("forceSelectBiomeIndex", -1),
        "startingCurrencyBagType": base_challenge.get("startingCurrencyBagType", "Bag"),
        "customOptionsString": "",
        "islands": [_island_from_level(base_level, "Island 1")],
    }


def normalize_design(design: dict) -> dict:
    design = dict(design or default_design())
    design.setdefault("schema", 1)
    design["name"] = str(design.get("name") or "My Custom Island")
    design["id"] = str(design.get("id") or f"custom.{slugify(design['name'])}")
    design.setdefault("description", "")
    design.setdefault("challengeSeed", 12345)
    design.setdefault("isMultiplayer", True)
    design.setdefault("includeHermits", True)
    design.setdefault("zombieMode", False)
    design.setdefault("forceSelectBiomeIndex", -1)
    design.setdefault("startingCurrencyBagType", "Bag")
    if not isinstance(design.get("islands"), list) or not design["islands"]:
        design["islands"] = [default_design()["islands"][0]]
    for idx, island in enumerate(design["islands"], start=1):
        island.setdefault("name", f"Island {idx}")
        island.setdefault("baseLevelConfig", design.get("baseLevelConfig", ""))
        for key, value in {
            "seasonChangeDays": 3,
            "islandMonumentID": -1,
            "startingCoins": 7,
            "startingBeggars": 0,
            "startingPeasants": 0,
            "startingCoinsContinueOverride": -1,
            "startingGems": 0,
            "incomeMultiplier": 1,
            "freeBoatParts": 0,
            "caveEscapeTimer": 20,
            "shouldPlayVictoryMusicOnGreedDefeat": True,
            "minLevelWidth": 520,
            "gemCount": 0,
            "twoCliffs": False,
            "caveless": False,
            "riverless": False,
            "randomizeCliffSide": False,
            "sideDistributionBias": 5,
        }.items():
            island.setdefault(key, value)
    return design


def export_design(design: dict, game_dir: Path | None = None) -> dict:
    design = normalize_design(design)
    EXPORT_DIR.mkdir(parents=True, exist_ok=True)
    filename = f"{slugify(design['name'])}.custom-challenge.json"
    workspace_path = EXPORT_DIR / filename
    workspace_path.write_text(json.dumps(design, indent=2), encoding="utf-8")

    game_path = None
    if game_dir is not None:
        target = game_dir / "UserData" / "KingdomMod" / "custom-challenges"
        target.mkdir(parents=True, exist_ok=True)
        game_path = target / filename
        game_path.write_text(json.dumps(design, indent=2), encoding="utf-8")

    return {
        "ok": True,
        "workspacePath": str(workspace_path),
        "gamePath": str(game_path) if game_path else None,
        "filename": filename,
    }


def _preferred(items: list[dict], name: str | None) -> dict | None:
    if not name:
        return None
    needle = name.replace("(Clone)", "").strip().lower()
    for item in items:
        actual = str(item.get("assetName", "")).replace("(Clone)", "").strip().lower()
        if actual == needle:
            return item
    return None


def _island_from_level(level: dict, name: str) -> dict:
    island = dict(level or {})
    island["name"] = name
    island["baseLevelConfig"] = island.pop("assetName", "")
    island.pop("landCycleData", None)
    island.pop("alternateLandCycleData", None)
    return island
