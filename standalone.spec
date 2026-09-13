# -*- mode: python ; coding: utf-8 -*-
from PyInstaller.utils.hooks import collect_all

pygame_datas, pygame_binaries, pygame_hidden = collect_all('pygame')

block_cipher = None

a = Analysis(
    ['app.py'],
    pathex=[],
    binaries=pygame_binaries,
    datas=pygame_datas,
    hiddenimports=pygame_hidden + [
        'PIL.Image',
        'PIL.ImageTk',
        'sqlite3',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='SoundCloudReleaseTracker',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
