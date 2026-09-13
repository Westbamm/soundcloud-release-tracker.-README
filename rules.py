from __future__ import annotations

from typing import Any, Iterable


def normalize_artist(value: str) -> str:
    return " ".join(str(value or "").strip().casefold().split())


def track_artist(track: dict[str, Any]) -> str:
    user = track.get("user") or {}
    return str(track.get("metadata_artist") or user.get("username") or "Unknown artist").strip()


def contains_artist(collection: Iterable[str], artist: str) -> bool:
    needle = normalize_artist(artist)
    return bool(needle) and needle in {normalize_artist(x) for x in collection if str(x).strip()}


def unique_artists(values: Iterable[str]) -> list[str]:
    result: list[str] = []
    seen: set[str] = set()
    for raw in values:
        value = " ".join(str(raw or "").strip().split())
        key = normalize_artist(value)
        if value and key not in seen:
            result.append(value)
            seen.add(key)
    return result


def sanitize_bpm_rules(raw: Any) -> dict[str, dict[str, int | None]]:
    if not isinstance(raw, dict):
        return {}
    result: dict[str, dict[str, int | None]] = {}
    for genre, value in raw.items():
        genre_name = " ".join(str(genre or "").strip().split())
        if not genre_name or not isinstance(value, dict):
            continue
        min_bpm = _optional_bpm(value.get("from"))
        max_bpm = _optional_bpm(value.get("to"))
        if min_bpm is not None and max_bpm is not None and min_bpm > max_bpm:
            min_bpm, max_bpm = max_bpm, min_bpm
        result[genre_name] = {"from": min_bpm, "to": max_bpm}
    return result


def bpm_rule_for_genre(
    rules: dict[str, dict[str, int | None]],
    genre: str,
    fallback_from: int | None = None,
    fallback_to: int | None = None,
) -> tuple[int | None, int | None]:
    target = " ".join(str(genre or "").strip().casefold().split())
    for name, rule in rules.items():
        if " ".join(name.strip().casefold().split()) == target:
            return rule.get("from"), rule.get("to")
    return fallback_from, fallback_to


def track_matches_bpm_rule(
    track: dict[str, Any],
    rules: dict[str, dict[str, int | None]],
    fallback_from: int | None = None,
    fallback_to: int | None = None,
) -> bool:
    genre = str(track.get("genre") or "")
    bpm_from, bpm_to = bpm_rule_for_genre(rules, genre, fallback_from, fallback_to)
    if bpm_from is None and bpm_to is None:
        return True
    try:
        bpm = int(track.get("bpm"))
    except (TypeError, ValueError):
        return False
    if bpm_from is not None and bpm < bpm_from:
        return False
    if bpm_to is not None and bpm > bpm_to:
        return False
    return True


def _optional_bpm(value: Any) -> int | None:
    if value in (None, ""):
        return None
    try:
        number = int(value)
    except (TypeError, ValueError):
        return None
    return number if 1 <= number <= 400 else None
