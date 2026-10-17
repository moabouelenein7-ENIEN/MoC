@echo off
title MoCSI - Structural Engineering Automation Suite (SBC 301 / ETABS)
echo ========================================================
echo   Launching MoCSI Structural Automation Suite (Python)
echo   Saudi Building Code (SBC 301 / 304) - CSI ETABS
echo ========================================================
cd /d "%~dp0"

if exist "%~dp0.venv\Scripts\python.exe" (
    "%~dp0.venv\Scripts\python.exe" run_mocsi.py
) else (
    python run_mocsi.py
)

if errorlevel 1 (
    echo.
    echo Application exited with an error.
    pause
)
