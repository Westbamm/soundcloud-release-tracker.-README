import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from database import TrackDatabase
from rules import (
    bpm_rule_for_genre,
    contains_artist,
    sanitize_bpm_rules,
    track_matches_bpm_rule,
    unique_artists,
)
from soundcloud_client import SoundCloudClient, safe_filename, track_urn


class CoreTests(unittest.TestCase):
    def test_safe_filename(self):
        self.assertEqual(safe_filename('A/B:C*D?"E'), "A_B_C_D__E")

    def test_track_urn_from_id(self):
        self.assertEqual(track_urn({"id": 123}), "soundcloud:tracks:123")

    def test_db_deduplicates_and_preserves_download(self):
        with tempfile.TemporaryDirectory() as tmp:
            db = TrackDatabase(Path(tmp) / "test.sqlite3")
            track = {
                "urn": "soundcloud:tracks:1",
                "title": "Test",
                "genre": "House",
                "created_at": "2026-09-13T12:00:00Z",
                "duration": 180000,
                "bpm": 124,
                "artwork_url": "https://example.test/art.jpg",
                "downloadable": True,
                "download_url": "https://api.soundcloud.com/tracks/1/download",
                "user": {"username": "Artist"},
            }
            self.assertTrue(db.upsert_track(track))
            self.assertFalse(db.upsert_track(track))
            db.mark_downloaded(track["urn"], Path(tmp) / "track.mp3")
            rows = db.list_tracks()
            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0]["bpm"], 124)
            self.assertEqual(rows[0]["artwork_url"], "https://example.test/art.jpg")
            self.assertTrue(rows[0]["downloaded_at"])

    def test_artist_lists_are_case_insensitive(self):
        artists = unique_artists(["CamelPhat", " camelphat ", "ARTBAT"])
        self.assertEqual(artists, ["CamelPhat", "ARTBAT"])
        self.assertTrue(contains_artist(artists, "CAMELPHAT"))
        self.assertFalse(contains_artist(artists, "Someone Else"))

    def test_genre_bpm_rules(self):
        rules = sanitize_bpm_rules({
            "Tech House": {"from": "124", "to": 130},
            "Drum & Bass": {"from": 180, "to": 170},
        })
        self.assertEqual(bpm_rule_for_genre(rules, "tech house", 100, 200), (124, 130))
        self.assertEqual(bpm_rule_for_genre(rules, "Ambient", 60, 100), (60, 100))
        self.assertEqual(rules["Drum & Bass"], {"from": 170, "to": 180})
        self.assertTrue(track_matches_bpm_rule({"genre": "Tech House", "bpm": 126}, rules))
        self.assertFalse(track_matches_bpm_rule({"genre": "Tech House", "bpm": 132}, rules))

    def test_preview_url_prefers_official_preview_stream(self):
        class FakeClient(SoundCloudClient):
            def get_streams(self, track):
                return {
                    "hls_aac_160_url": "https://example.test/full.m3u8",
                    "preview_mp3_128_url": "https://example.test/preview.mp3",
                }

        client = FakeClient("id", "secret")
        url = client.get_preview_url({"urn": "soundcloud:tracks:1"})
        self.assertEqual(url, "https://example.test/preview.mp3")


if __name__ == "__main__":
    unittest.main()
