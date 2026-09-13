from __future__ import annotations

import json
import sqlite3
from contextlib import closing
from pathlib import Path
from typing import Any

from soundcloud_client import track_urn


class TrackDatabase:
    def __init__(self, path: Path):
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._init_db()

    def _connect(self) -> sqlite3.Connection:
        con = sqlite3.connect(self.path, timeout=15)
        con.row_factory = sqlite3.Row
        return con

    def _init_db(self) -> None:
        with closing(self._connect()) as con:
            with con:
                con.executescript(
                    """
                    PRAGMA journal_mode=WAL;
                    CREATE TABLE IF NOT EXISTS tracks (
                        urn TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        artist TEXT,
                        genre TEXT,
                        created_at TEXT,
                        permalink_url TEXT,
                        artwork_url TEXT,
                        duration INTEGER,
                        bpm INTEGER,
                        downloadable INTEGER NOT NULL DEFAULT 0,
                        download_url TEXT,
                        first_seen_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        downloaded_at TEXT,
                        download_path TEXT,
                        raw_json TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_tracks_created_at ON tracks(created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_tracks_genre ON tracks(genre);
                    CREATE INDEX IF NOT EXISTS idx_tracks_bpm ON tracks(bpm);
                    """
                )

    def upsert_track(self, track: dict[str, Any]) -> bool:
        urn = track_urn(track)
        user = track.get("user") or {}
        artist = track.get("metadata_artist") or user.get("username") or "Unknown artist"
        values = (
            urn,
            str(track.get("title") or "Untitled"),
            str(artist),
            str(track.get("genre") or ""),
            str(track.get("created_at") or ""),
            str(track.get("permalink_url") or ""),
            str(track.get("artwork_url") or ""),
            int(track.get("duration") or 0),
            int(track["bpm"]) if track.get("bpm") not in (None, "") else None,
            1 if track.get("downloadable") else 0,
            str(track.get("download_url") or ""),
            json.dumps(track, ensure_ascii=False),
        )
        with closing(self._connect()) as con:
            with con:
                existed = con.execute("SELECT 1 FROM tracks WHERE urn = ?", (urn,)).fetchone() is not None
                con.execute(
                    """
                    INSERT INTO tracks (
                        urn, title, artist, genre, created_at, permalink_url, artwork_url,
                        duration, bpm, downloadable, download_url, raw_json
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(urn) DO UPDATE SET
                        title=excluded.title,
                        artist=excluded.artist,
                        genre=excluded.genre,
                        created_at=excluded.created_at,
                        permalink_url=excluded.permalink_url,
                        artwork_url=excluded.artwork_url,
                        duration=excluded.duration,
                        bpm=excluded.bpm,
                        downloadable=excluded.downloadable,
                        download_url=excluded.download_url,
                        raw_json=excluded.raw_json
                    """,
                    values,
                )
        return not existed

    def mark_downloaded(self, urn: str, path: Path) -> None:
        with closing(self._connect()) as con:
            with con:
                con.execute(
                    "UPDATE tracks SET downloaded_at=CURRENT_TIMESTAMP, download_path=? WHERE urn=?",
                    (str(path), urn),
                )

    def list_tracks(self, limit: int = 1000) -> list[dict[str, Any]]:
        with closing(self._connect()) as con:
            rows = con.execute(
                """
                SELECT urn, title, artist, genre, created_at, permalink_url, artwork_url,
                       duration, bpm, downloadable, downloaded_at, download_path, raw_json
                FROM tracks
                ORDER BY created_at DESC, first_seen_at DESC
                LIMIT ?
                """,
                (limit,),
            ).fetchall()
        result: list[dict[str, Any]] = []
        for row in rows:
            item = dict(row)
            try:
                item["raw"] = json.loads(item.pop("raw_json"))
            except Exception:
                item["raw"] = {}
            result.append(item)
        return result

    def get_track(self, urn: str) -> dict[str, Any] | None:
        with closing(self._connect()) as con:
            row = con.execute("SELECT raw_json FROM tracks WHERE urn=?", (urn,)).fetchone()
        if not row:
            return None
        try:
            return json.loads(row["raw_json"])
        except Exception:
            return None
