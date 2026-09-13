from __future__ import annotations

import json
import mimetypes
import os
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


API_BASE = "https://api.soundcloud.com"
TOKEN_URL = "https://secure.soundcloud.com/oauth/token"
USER_AGENT = "SoundCloudReleaseTracker/0.3"


class SoundCloudError(RuntimeError):
    pass


@dataclass
class DownloadResult:
    path: Path
    bytes_written: int


def safe_filename(value: str, max_length: int = 160) -> str:
    value = re.sub(r"[\\/:*?\"<>|\x00-\x1f]", "_", value).strip().strip(".")
    value = re.sub(r"\s+", " ", value)
    if not value:
        value = "track"
    return value[:max_length].rstrip()


def track_urn(track: dict[str, Any]) -> str:
    urn = track.get("urn")
    if urn:
        return str(urn)
    track_id = track.get("id")
    if track_id is not None:
        return f"soundcloud:tracks:{track_id}"
    raise SoundCloudError("SoundCloud вернул трек без urn/id")


class SoundCloudClient:
    def __init__(self, client_id: str, client_secret: str, timeout: int = 30):
        self.client_id = client_id.strip()
        self.client_secret = client_secret.strip()
        self.timeout = timeout
        self._access_token: str | None = None
        self._refresh_token: str | None = None
        self._token_expiry = 0.0

    def _form_request(self, url: str, data: dict[str, str]) -> dict[str, Any]:
        payload = urllib.parse.urlencode(data).encode("utf-8")
        req = urllib.request.Request(
            url,
            data=payload,
            headers={
                "Accept": "application/json; charset=utf-8",
                "Content-Type": "application/x-www-form-urlencoded",
                "User-Agent": USER_AGENT,
            },
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise SoundCloudError(f"OAuth ошибка SoundCloud ({exc.code}): {body[:500]}") from exc
        except urllib.error.URLError as exc:
            raise SoundCloudError(f"Не удалось подключиться к SoundCloud: {exc.reason}") from exc

    def _obtain_token(self) -> str:
        if not self.client_id or not self.client_secret:
            raise SoundCloudError("Укажите Client ID и Client Secret в настройках.")

        now = time.time()
        if self._access_token and now < self._token_expiry - 60:
            return self._access_token

        if self._refresh_token:
            try:
                result = self._form_request(
                    TOKEN_URL,
                    {
                        "grant_type": "refresh_token",
                        "client_id": self.client_id,
                        "client_secret": self.client_secret,
                        "refresh_token": self._refresh_token,
                    },
                )
                self._store_token(result)
                return self._access_token or ""
            except SoundCloudError:
                self._refresh_token = None

        result = self._form_request(
            TOKEN_URL,
            {
                "grant_type": "client_credentials",
                "client_id": self.client_id,
                "client_secret": self.client_secret,
            },
        )
        self._store_token(result)
        if not self._access_token:
            raise SoundCloudError("SoundCloud не вернул access_token.")
        return self._access_token

    def _store_token(self, result: dict[str, Any]) -> None:
        token = result.get("access_token")
        if not token:
            raise SoundCloudError("SoundCloud не вернул access_token.")
        self._access_token = str(token)
        refresh = result.get("refresh_token")
        if refresh:
            self._refresh_token = str(refresh)
        try:
            expires_in = int(result.get("expires_in", 3600))
        except (TypeError, ValueError):
            expires_in = 3600
        self._token_expiry = time.time() + max(300, expires_in)

    def _request_json(self, url: str, retry_auth: bool = True) -> Any:
        token = self._obtain_token()
        req = urllib.request.Request(
            url,
            headers={
                "Accept": "application/json; charset=utf-8",
                "Authorization": f"OAuth {token}",
                "User-Agent": USER_AGENT,
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            if exc.code == 401 and retry_auth:
                self._access_token = None
                self._token_expiry = 0
                return self._request_json(url, retry_auth=False)
            if exc.code == 429:
                retry_after = exc.headers.get("Retry-After")
                suffix = f" Повторите через {retry_after} сек." if retry_after else ""
                raise SoundCloudError(f"Достигнут лимит запросов SoundCloud (429).{suffix}") from exc
            raise SoundCloudError(f"SoundCloud API ({exc.code}): {body[:500]}") from exc
        except urllib.error.URLError as exc:
            raise SoundCloudError(f"Ошибка сети: {exc.reason}") from exc

    def search_tracks(
        self,
        genres: Iterable[str],
        created_from: str | None = None,
        bpm_from: int | None = None,
        bpm_to: int | None = None,
        duration_from_ms: int | None = None,
        duration_to_ms: int | None = None,
        limit: int = 100,
        max_pages: int = 3,
    ) -> list[dict[str, Any]]:
        genre_list = [g.strip() for g in genres if g and g.strip()]
        params: dict[str, str] = {
            "linked_partitioning": "true",
            "limit": str(max(1, min(limit, 200))),
            "access": "playable,preview",
        }
        if genre_list:
            params["genres"] = ",".join(genre_list)
        if created_from:
            params["created_at[from]"] = created_from
        if bpm_from is not None:
            params["bpm[from]"] = str(bpm_from)
        if bpm_to is not None:
            params["bpm[to]"] = str(bpm_to)
        if duration_from_ms is not None:
            params["duration[from]"] = str(duration_from_ms)
        if duration_to_ms is not None:
            params["duration[to]"] = str(duration_to_ms)

        url = f"{API_BASE}/tracks?{urllib.parse.urlencode(params)}"
        collected: list[dict[str, Any]] = []
        pages = 0

        while url and pages < max_pages:
            payload = self._request_json(url)
            pages += 1
            if isinstance(payload, list):
                collected.extend(x for x in payload if isinstance(x, dict))
                break
            if not isinstance(payload, dict):
                break
            collection = payload.get("collection", [])
            if isinstance(collection, list):
                collected.extend(x for x in collection if isinstance(x, dict))
            next_href = payload.get("next_href")
            url = str(next_href) if next_href else ""

        def created_key(track: dict[str, Any]) -> str:
            return str(track.get("created_at") or "")

        collected.sort(key=created_key, reverse=True)
        return collected

    def get_streams(self, track: dict[str, Any]) -> dict[str, Any]:
        urn = urllib.parse.quote(track_urn(track), safe=":")
        payload = self._request_json(f"{API_BASE}/tracks/{urn}/streams")
        if not isinstance(payload, dict):
            raise SoundCloudError("SoundCloud не вернул список потоков для трека.")
        return payload

    def get_preview_url(self, track: dict[str, Any]) -> str:
        streams = self.get_streams(track)
        preview = streams.get("preview_mp3_128_url")
        if not preview:
            raise SoundCloudError("Для этого трека SoundCloud не предоставляет preview-фрагмент.")
        return str(preview)

    def cache_preview(self, track: dict[str, Any], cache_dir: Path) -> Path:
        cache_dir.mkdir(parents=True, exist_ok=True)
        urn = track_urn(track)
        safe_urn = safe_filename(urn.replace(":", "_"), max_length=120)
        path = cache_dir / f"{safe_urn}.preview.mp3"
        if path.exists() and path.stat().st_size > 1024:
            return path

        preview_url = self.get_preview_url(track)
        req = urllib.request.Request(
            preview_url,
            headers={
                "User-Agent": USER_AGENT,
                "Accept": "audio/mpeg,*/*;q=0.8",
            },
        )
        tmp = path.with_suffix(path.suffix + ".tmp")
        try:
            with urllib.request.urlopen(req, timeout=max(self.timeout, 45)) as response, tmp.open("wb") as out:
                written = 0
                while True:
                    chunk = response.read(128 * 1024)
                    if not chunk:
                        break
                    out.write(chunk)
                    written += len(chunk)
                    if written > 8 * 1024 * 1024:
                        raise SoundCloudError("Preview SoundCloud оказался неожиданно большим.")
            if tmp.stat().st_size < 1024:
                raise SoundCloudError("SoundCloud вернул пустой preview-файл.")
            tmp.replace(path)
            return path
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise SoundCloudError(f"Ошибка preview ({exc.code}): {body[:300]}") from exc
        except urllib.error.URLError as exc:
            raise SoundCloudError(f"Ошибка сети при загрузке preview: {exc.reason}") from exc
        finally:
            try:
                if tmp.exists():
                    tmp.unlink()
            except OSError:
                pass

    def download_track(self, track: dict[str, Any], destination_dir: Path) -> DownloadResult:
        if not track.get("downloadable") or not track.get("download_url"):
            raise SoundCloudError("Автор не разрешил скачивание этого трека через SoundCloud.")

        download_url = str(track["download_url"])
        token = self._obtain_token()
        req = urllib.request.Request(
            download_url,
            headers={
                "Authorization": f"OAuth {token}",
                "User-Agent": USER_AGENT,
                "Accept": "*/*",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=max(self.timeout, 120)) as response:
                content_type = response.headers.get_content_type()
                content_disp = response.headers.get("Content-Disposition", "")
                filename = self._filename_from_headers(content_disp)
                if not filename:
                    artist = ((track.get("user") or {}).get("username") or track.get("metadata_artist") or "Unknown artist")
                    title = track.get("title") or "track"
                    extension = mimetypes.guess_extension(content_type) or ".mp3"
                    if extension == ".jpe":
                        extension = ".jpg"
                    filename = f"{safe_filename(str(artist))} - {safe_filename(str(title))}{extension}"

                genre = safe_filename(str(track.get("genre") or "Other"), max_length=80)
                folder = destination_dir / genre
                folder.mkdir(parents=True, exist_ok=True)
                path = self._unique_path(folder / safe_filename(filename, max_length=220))

                bytes_written = 0
                with path.open("wb") as out:
                    while True:
                        chunk = response.read(1024 * 256)
                        if not chunk:
                            break
                        out.write(chunk)
                        bytes_written += len(chunk)
                return DownloadResult(path=path, bytes_written=bytes_written)
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise SoundCloudError(f"Ошибка скачивания ({exc.code}): {body[:400]}") from exc
        except urllib.error.URLError as exc:
            raise SoundCloudError(f"Ошибка сети при скачивании: {exc.reason}") from exc

    @staticmethod
    def _filename_from_headers(content_disposition: str) -> str | None:
        if not content_disposition:
            return None
        star = re.search(r"filename\*=UTF-8''([^;]+)", content_disposition, flags=re.I)
        if star:
            return safe_filename(urllib.parse.unquote(star.group(1)))
        plain = re.search(r'filename="?([^";]+)"?', content_disposition, flags=re.I)
        if plain:
            return safe_filename(plain.group(1).strip())
        return None

    @staticmethod
    def _unique_path(path: Path) -> Path:
        if not path.exists():
            return path
        stem, suffix = path.stem, path.suffix
        for index in range(2, 10000):
            candidate = path.with_name(f"{stem} ({index}){suffix}")
            if not candidate.exists():
                return candidate
        raise SoundCloudError("Не удалось подобрать свободное имя файла.")
