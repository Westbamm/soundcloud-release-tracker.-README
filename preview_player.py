from __future__ import annotations

import threading
from pathlib import Path
from typing import Callable


class PreviewPlayerError(RuntimeError):
    pass


class PreviewPlayer:
    """Small pygame-backed player for SoundCloud's official preview MP3 snippets."""

    def __init__(self, on_finished: Callable[[], None] | None = None) -> None:
        self.on_finished = on_finished
        self._pygame = None
        self._initialized = False
        self._watch_generation = 0
        self._lock = threading.Lock()

    def _ensure_init(self) -> None:
        if self._initialized:
            return
        try:
            import pygame
        except ImportError as exc:
            raise PreviewPlayerError("Для встроенного preview установите зависимость pygame.") from exc
        try:
            pygame.mixer.init()
        except Exception as exc:
            raise PreviewPlayerError(f"Не удалось инициализировать аудио: {exc}") from exc
        self._pygame = pygame
        self._initialized = True

    def play(self, path: Path, volume: float = 0.8) -> None:
        self._ensure_init()
        if not path.exists():
            raise PreviewPlayerError("Preview-файл не найден.")
        assert self._pygame is not None
        with self._lock:
            self._watch_generation += 1
            generation = self._watch_generation
            try:
                self._pygame.mixer.music.stop()
                self._pygame.mixer.music.load(str(path))
                self._pygame.mixer.music.set_volume(max(0.0, min(float(volume), 1.0)))
                self._pygame.mixer.music.play()
            except Exception as exc:
                raise PreviewPlayerError(f"Не удалось воспроизвести preview: {exc}") from exc
        threading.Thread(target=self._watch_finished, args=(generation,), daemon=True).start()

    def _watch_finished(self, generation: int) -> None:
        if self._pygame is None:
            return
        clock = self._pygame.time.Clock()
        while True:
            with self._lock:
                if generation != self._watch_generation:
                    return
                busy = bool(self._pygame.mixer.music.get_busy())
            if not busy:
                break
            clock.tick(5)
        if self.on_finished:
            try:
                self.on_finished()
            except Exception:
                pass

    def pause(self) -> None:
        self._ensure_init()
        assert self._pygame is not None
        self._pygame.mixer.music.pause()

    def resume(self) -> None:
        self._ensure_init()
        assert self._pygame is not None
        self._pygame.mixer.music.unpause()

    def stop(self) -> None:
        if not self._initialized or self._pygame is None:
            return
        with self._lock:
            self._watch_generation += 1
            self._pygame.mixer.music.stop()

    def set_volume(self, volume: float) -> None:
        if not self._initialized or self._pygame is None:
            return
        self._pygame.mixer.music.set_volume(max(0.0, min(float(volume), 1.0)))

    def is_playing(self) -> bool:
        return bool(self._initialized and self._pygame is not None and self._pygame.mixer.music.get_busy())

    def close(self) -> None:
        if not self._initialized or self._pygame is None:
            return
        try:
            self.stop()
            self._pygame.mixer.quit()
        finally:
            self._initialized = False
            self._pygame = None
