@echo off
setlocal
cd /d "%~dp0"

echo SoundCloud Release Tracker v4 - Standalone EXE build
echo.
echo NOTE: This script is only for developers/build machines.
echo The finished EXE does NOT require Python on the user's PC.
echo.

where python >nul 2>nul
if errorlevel 1 (
  echo Python was not found on this BUILD machine.
  echo Use the included GitHub Actions workflow instead; it builds on a Windows cloud runner.
  pause
  exit /b 1
)

if not exist .venv python -m venv .venv
call .venv\Scripts\activate.bat
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
python -m unittest discover -s tests -v
if errorlevel 1 goto :fail
python -m PyInstaller --noconfirm --clean standalone.spec
if errorlevel 1 goto :fail

echo.
echo READY: dist\SoundCloudReleaseTracker.exe
echo This EXE can run on Windows without Python installed.
pause
exit /b 0

:fail
echo.
echo Build failed.
pause
exit /b 1
