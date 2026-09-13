from __future__ import annotations

import json
import threading
import webbrowser
from datetime import datetime, timedelta, timezone
from pathlib import Path
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from config_store import DB_PATH, PREVIEW_CACHE_DIR, load_config, save_config
from database import TrackDatabase
from preview_player import PreviewPlayer, PreviewPlayerError
from rules import contains_artist, sanitize_bpm_rules, track_artist, unique_artists
from soundcloud_client import SoundCloudClient, SoundCloudError, track_urn

APP_TITLE = "SoundCloud Release Tracker v4"
GENRES = ["House","Tech House","Deep House","Afro House","Progressive House","Techno",
          "Melodic Techno","Trance","Drum & Bass","Dubstep","Hip Hop","Trap","Ambient","Electronic"]


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("1250x780")
        self.minsize(1000, 650)
        self.cfg = load_config()
        self.db = TrackDatabase(DB_PATH)
        self.player = PreviewPlayer(on_finished=lambda: self.after(0, self._preview_finished))
        self.monitoring = False
        self.busy = False
        self.rows: dict[str, dict] = {}
        self._build()
        self._load_cfg()
        self._refresh()

    def _build(self):
        root = ttk.Frame(self, padding=12)
        root.pack(fill="both", expand=True)

        top = ttk.Frame(root)
        top.pack(fill="x", pady=(0, 8))
        ttk.Label(top, text=APP_TITLE, font=("Segoe UI", 18, "bold")).pack(side="left")
        self.status = tk.StringVar(value="Готово")
        ttk.Label(top, textvariable=self.status).pack(side="right")

        book = ttk.Notebook(root)
        book.pack(fill="both", expand=True)
        tracks = ttk.Frame(book, padding=8)
        artists = ttk.Frame(book, padding=8)
        settings = ttk.Frame(book, padding=8)
        book.add(tracks, text="Новые треки")
        book.add(artists, text="Артисты и BPM")
        book.add(settings, text="Настройки")

        bar = ttk.Frame(tracks)
        bar.pack(fill="x", pady=(0, 8))
        ttk.Button(bar, text="Проверить сейчас", command=self.check_now).pack(side="left")
        self.monitor_btn = ttk.Button(bar, text="▶ Мониторинг", command=self.toggle_monitor)
        self.monitor_btn.pack(side="left", padx=6)
        ttk.Button(bar, text="▶ Preview", command=self.preview).pack(side="left", padx=6)
        ttk.Button(bar, text="■ Stop", command=self.stop_preview).pack(side="left")
        ttk.Button(bar, text="Скачать", command=self.download).pack(side="left", padx=6)
        ttk.Button(bar, text="Открыть SoundCloud", command=self.open_sc).pack(side="left")

        self.only_fav = tk.BooleanVar(value=False)
        self.only_download = tk.BooleanVar(value=False)
        ttk.Checkbutton(bar, text="★ Любимые", variable=self.only_fav, command=self._refresh).pack(side="right")
        ttk.Checkbutton(bar, text="Только download", variable=self.only_download, command=self._refresh).pack(side="right", padx=8)

        cols = ("created","artist","title","genre","bpm","download")
        self.tree = ttk.Treeview(tracks, columns=cols, show="headings", selectmode="browse")
        widths = {"created":150,"artist":190,"title":360,"genre":150,"bpm":70,"download":85}
        labels = {"created":"Дата","artist":"Артист","title":"Название","genre":"Жанр","bpm":"BPM","download":"Download"}
        for c in cols:
            self.tree.heading(c, text=labels[c])
            self.tree.column(c, width=widths[c], anchor="w")
        self.tree.pack(fill="both", expand=True)
        self.tree.bind("<Double-1>", lambda _e: self.preview())

        af = ttk.LabelFrame(artists, text="Любимые артисты", padding=8)
        af.pack(fill="both", expand=True, side="left", padx=(0,6))
        self.favorites = tk.Text(af, height=16, width=35)
        self.favorites.pack(fill="both", expand=True)
        bf = ttk.LabelFrame(artists, text="Blacklist", padding=8)
        bf.pack(fill="both", expand=True, side="left", padx=6)
        self.blacklist = tk.Text(bf, height=16, width=35)
        self.blacklist.pack(fill="both", expand=True)
        rf = ttk.LabelFrame(artists, text="BPM-правила по жанрам", padding=8)
        rf.pack(fill="both", expand=True, side="left", padx=(6,0))
        ttk.Label(rf, text="Формат: Жанр=MIN-MAX\nНапример: Tech House=124-130").pack(anchor="w")
        self.rules = tk.Text(rf, height=16, width=35)
        self.rules.pack(fill="both", expand=True, pady=6)
        ttk.Button(artists, text="Сохранить списки и BPM", command=self.save).pack(side="bottom", pady=10)

        form = ttk.Frame(settings)
        form.pack(anchor="nw", fill="x")
        self.client_id = tk.StringVar()
        self.client_secret = tk.StringVar()
        self.genres = tk.StringVar()
        self.poll = tk.StringVar(value="15")
        self.lookback = tk.StringVar(value="24")
        self.download_dir = tk.StringVar()
        self.auto_download = tk.BooleanVar(value=False)
        fields = [
            ("Client ID", self.client_id, False),
            ("Client Secret", self.client_secret, True),
            ("Жанры через запятую", self.genres, False),
            ("Проверять каждые, мин", self.poll, False),
            ("Искать за последние, ч", self.lookback, False),
            ("Папка загрузки", self.download_dir, False),
        ]
        for r,(label,var,secret) in enumerate(fields):
            ttk.Label(form, text=label).grid(row=r,column=0,sticky="w",padx=(0,8),pady=5)
            ttk.Entry(form, textvariable=var, width=75, show="*" if secret else "").grid(row=r,column=1,sticky="ew",pady=5)
        form.columnconfigure(1, weight=1)
        ttk.Checkbutton(form, text="Автоматически скачивать только официально downloadable треки",
                        variable=self.auto_download).grid(row=len(fields),column=1,sticky="w",pady=8)
        buttons = ttk.Frame(settings)
        buttons.pack(anchor="w", pady=12)
        ttk.Button(buttons, text="Выбрать папку", command=self.choose_dir).pack(side="left")
        ttk.Button(buttons, text="Проверить API", command=self.test_api).pack(side="left", padx=8)
        ttk.Button(buttons, text="Сохранить настройки", command=self.save).pack(side="left")

    def _lines(self, widget: tk.Text):
        return [x.strip() for x in widget.get("1.0","end").splitlines() if x.strip()]

    def _parse_rules(self):
        out = {}
        for line in self._lines(self.rules):
            if "=" not in line:
                continue
            genre, rng = line.split("=",1)
            try:
                a,b = rng.split("-",1)
                out[genre.strip()] = {"from": int(a.strip()), "to": int(b.strip())}
            except Exception:
                continue
        return sanitize_bpm_rules(out)

    def _load_cfg(self):
        self.client_id.set(self.cfg.get("client_id",""))
        self.client_secret.set(self.cfg.get("client_secret",""))
        self.genres.set(", ".join(self.cfg.get("genres", GENRES[:4])))
        self.poll.set(str(self.cfg.get("poll_minutes",15)))
        self.lookback.set(str(self.cfg.get("lookback_hours",24)))
        self.download_dir.set(str(self.cfg.get("download_dir", str(Path.home()/"Music"/"SoundCloud New Releases"))))
        self.auto_download.set(bool(self.cfg.get("auto_download",False)))
        self.favorites.insert("1.0","\n".join(self.cfg.get("favorite_artists",[])))
        self.blacklist.insert("1.0","\n".join(self.cfg.get("blacklisted_artists",[])))
        rules = self.cfg.get("genre_bpm_rules",{})
        self.rules.insert("1.0","\n".join(
            f"{g}={r.get('from','')}-{r.get('to','')}" for g,r in rules.items()
        ))

    def collect_cfg(self):
        cfg = dict(self.cfg)
        cfg.update({
            "client_id": self.client_id.get().strip(),
            "client_secret": self.client_secret.get().strip(),
            "genres": [g.strip() for g in self.genres.get().split(",") if g.strip()],
            "poll_minutes": max(1, int(self.poll.get() or 15)),
            "lookback_hours": max(1, int(self.lookback.get() or 24)),
            "download_dir": self.download_dir.get().strip(),
            "auto_download": bool(self.auto_download.get()),
            "favorite_artists": unique_artists(self._lines(self.favorites)),
            "blacklisted_artists": unique_artists(self._lines(self.blacklist)),
            "genre_bpm_rules": self._parse_rules(),
        })
        return cfg

    def save(self):
        try:
            self.cfg = self.collect_cfg()
            save_config(self.cfg)
            self.status.set("Настройки сохранены")
            self._refresh()
        except Exception as e:
            messagebox.showerror("Ошибка", str(e))

    def choose_dir(self):
        p = filedialog.askdirectory(initialdir=self.download_dir.get() or str(Path.home()))
        if p:
            self.download_dir.set(p)

    def client(self, cfg=None):
        cfg = cfg or self.collect_cfg()
        return SoundCloudClient(cfg["client_id"], cfg["client_secret"])

    def test_api(self):
        try:
            cfg = self.collect_cfg()
        except Exception as e:
            messagebox.showerror("Настройки", str(e))
            return
        if not cfg.get("client_id") or not cfg.get("client_secret"):
            messagebox.showwarning(
                "SoundCloud API",
                "Введите Client ID и Client Secret во вкладке «Настройки»."
            )
            return
        self.status.set("Проверяю API…")
        def work():
            try:
                c = self.client(cfg)
                c.search_tracks([], limit=1, max_pages=1)
                self.after(0, lambda: self.status.set("API подключён"))
                self.after(0, lambda: messagebox.showinfo("SoundCloud", "API подключён успешно."))
            except Exception as e:
                msg = str(e)
                self.after(0, lambda msg=msg: messagebox.showerror("SoundCloud", msg))
        threading.Thread(target=work, daemon=True).start()

    def check_now(self):
        if self.busy:
            return
        try:
            cfg = self.collect_cfg()
            self.cfg = cfg
            save_config(cfg)
        except Exception as e:
            messagebox.showerror("Настройки", str(e))
            return
        if not cfg.get("client_id") or not cfg.get("client_secret"):
            messagebox.showwarning(
                "SoundCloud API",
                "Сначала откройте вкладку «Настройки» и укажите Client ID и Client Secret SoundCloud."
            )
            self.status.set("Нужны API-ключи")
            return

        self.busy = True
        self.status.set("Проверяю SoundCloud…")

        def work():
            new_count = 0
            downloaded = 0
            try:
                c = self.client(cfg)
                created = (datetime.now(timezone.utc)-timedelta(hours=cfg["lookback_hours"])).isoformat().replace("+00:00","Z")
                seen = set()
                for genre in cfg["genres"]:
                    self.after(0, lambda genre=genre: self.status.set(f"Ищу: {genre}…"))
                    rule = cfg["genre_bpm_rules"].get(genre,{})
                    tracks = c.search_tracks([genre], created_from=created,
                                             bpm_from=rule.get("from"), bpm_to=rule.get("to"),
                                             limit=100, max_pages=2)
                    for t in tracks:
                        urn = track_urn(t)
                        if urn in seen:
                            continue
                        seen.add(urn)
                        artist = track_artist(t)
                        if contains_artist(cfg["blacklisted_artists"], artist):
                            continue
                        is_new = self.db.upsert_track(t)
                        if is_new:
                            new_count += 1
                            if cfg["auto_download"] and t.get("downloadable") and t.get("download_url"):
                                try:
                                    result = c.download_track(t, Path(cfg["download_dir"]).expanduser())
                                    self.db.mark_downloaded(urn, result.path)
                                    downloaded += 1
                                except Exception:
                                    pass
                self.after(0, lambda: self._done_check(new_count, downloaded))
            except Exception as e:
                msg = str(e)
                self.after(0, lambda msg=msg: self._fail(msg))
        threading.Thread(target=work, daemon=True).start()

    def _done_check(self, n, d):
        self.busy = False
        self.status.set(f"Новых: {n}" + (f" • скачано: {d}" if d else ""))
        self._refresh()

    def _fail(self, msg):
        self.busy = False
        short = msg.replace("\n", " ").strip()
        self.status.set("Ошибка: " + (short[:90] + ("…" if len(short) > 90 else "")))
        messagebox.showerror("SoundCloud", msg)

    def toggle_monitor(self):
        self.monitoring = not self.monitoring
        self.monitor_btn.configure(text="■ Остановить" if self.monitoring else "▶ Мониторинг")
        if self.monitoring:
            self.check_now()
            self._schedule_monitor()

    def _schedule_monitor(self):
        if not self.monitoring:
            return
        try:
            ms = max(1, int(self.poll.get() or 15))*60*1000
        except Exception:
            ms = 15*60*1000
        self.after(ms, self._monitor_tick)

    def _monitor_tick(self):
        if not self.monitoring:
            return
        self.check_now()
        self._schedule_monitor()

    def _refresh(self):
        for iid in self.tree.get_children():
            self.tree.delete(iid)
        self.rows.clear()
        cfg = self.cfg
        favorites = cfg.get("favorite_artists",[])
        for item in self.db.list_tracks(1500):
            raw = item.get("raw") or {}
            artist = item.get("artist") or track_artist(raw)
            if contains_artist(cfg.get("blacklisted_artists",[]), artist):
                continue
            if self.only_fav.get() and not contains_artist(favorites, artist):
                continue
            if self.only_download.get() and not item.get("downloadable"):
                continue
            urn = item["urn"]
            self.rows[urn] = raw
            created = str(item.get("created_at") or "").replace("T"," ")[:16]
            self.tree.insert("", "end", iid=urn, values=(
                created, artist, item.get("title",""), item.get("genre",""),
                item.get("bpm") or "", "yes" if item.get("downloadable") else ""
            ))

    def selected(self):
        sel = self.tree.selection()
        return self.rows.get(sel[0]) if sel else None

    def preview(self):
        t = self.selected()
        if not t:
            return
        self.status.set("Готовлю preview…")
        def work():
            try:
                c = self.client()
                path = c.cache_preview(t, PREVIEW_CACHE_DIR)
                self.after(0, lambda: self._play(path))
            except Exception as e:
                msg = str(e)
                self.after(0, lambda msg=msg: self._fail(msg))
        threading.Thread(target=work, daemon=True).start()

    def _play(self, path):
        try:
            self.player.play(Path(path), 0.8)
            self.status.set("Preview играет")
        except PreviewPlayerError as e:
            messagebox.showerror("Preview", str(e))

    def stop_preview(self):
        self.player.stop()
        self.status.set("Preview остановлен")

    def _preview_finished(self):
        self.status.set("Preview завершён")

    def open_sc(self):
        t = self.selected()
        if t and t.get("permalink_url"):
            webbrowser.open(str(t["permalink_url"]))

    def download(self):
        t = self.selected()
        if not t:
            return
        if not t.get("downloadable") or not t.get("download_url"):
            messagebox.showinfo("Download", "Автор не разрешил официальное скачивание этого трека.")
            return
        self.status.set("Скачиваю…")
        def work():
            try:
                cfg = self.collect_cfg()
                result = self.client(cfg).download_track(t, Path(cfg["download_dir"]).expanduser())
                self.db.mark_downloaded(track_urn(t), result.path)
                self.after(0, lambda: messagebox.showinfo("Готово", f"Сохранено:\n{result.path}"))
                self.after(0, lambda: self.status.set("Трек скачан"))
            except Exception as e:
                msg = str(e)
                self.after(0, lambda msg=msg: self._fail(msg))
        threading.Thread(target=work, daemon=True).start()

    def destroy(self):
        try:
            self.player.close()
        except Exception:
            pass
        super().destroy()


if __name__ == "__main__":
    App().mainloop()
