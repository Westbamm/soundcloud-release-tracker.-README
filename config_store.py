from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

from rules import sanitize_bpm_rules, unique_artists


APP_DIR = Path.home() / ".soundcloud_release_tracker"
CONFIG_PATH = APP_DIR / "config.json"
DB_PATH = APP_DIR / "tracks.sqlite3"
PREVIEW_CACHE_DIR = APP_DIR / "preview_cache"
DEFAULT_DOWNLOAD_DIR = Path.home() / "Music" / "SoundCloud New Releases"

DEFAULT_CONFIG: dict[str, Any] = {
    "client_id": "",
    "client_secret": "",
    "genres": ["House", "Tech House", "Afro House"],
    "poll_minutes": 15,
    "lookback_hours": 24,
    "bpm_from": None,
    "bpm_to": None,
    "genre_bpm_rules": {},
    "favorite_artists": [],
    "blacklisted_artists": [],
    "hide_blacklisted": True,
    "preview_volume": 80,
    "notifications": True,
    "auto_download": False,
    "download_dir": str(DEFAULT_DOWNLOAD_DIR),
    "last_checked_utc": "",
}


def load_config() -> dict[str, Any]:
    APP_DIR.mkdir(parents=True, exist_ok=True)
    if not CONFIG_PATH.exists():
        return dict(DEFAULT_CONFIG)
    try:
        loaded = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        result = dict(DEFAULT_CONFIG)
        if isinstance(loaded, dict):
            result.update(loaded)
        result["favorite_artists"] = unique_artists(result.get("favorite_artists", []))
        result["blacklisted_artists"] = unique_artists(result.get("blacklisted_artists", []))
        result["genre_bpm_rules"] = sanitize_bpm_rules(result.get("genre_bpm_rules", {}))
        return result
    except Exception:
        return dict(DEFAULT_CONFIG)


def save_config(config: dict[str, Any]) -> None:
    APP_DIR.mkdir(parents=True, exist_ok=True)
    payload = dict(config)
    payload["favorite_artists"] = unique_artists(payload.get("favorite_artists", []))
    payload["blacklisted_artists"] = unique_artists(payload.get("blacklisted_artists", []))
    payload["genre_bpm_rules"] = sanitize_bpm_rules(payload.get("genre_bpm_rules", {}))
    tmp = CONFIG_PATH.with_suffix(".tmp")
    tmp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    try:
        os.chmod(tmp, 0o600)
    except OSError:
        pass
    tmp.replace(CONFIG_PATH)
