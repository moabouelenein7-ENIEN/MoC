@echo off
title MoCSI - Structural Engineering Automation Suite (SBC 301 / ETABS)
echo ========================================================
echo   Launching MoCSI Structural Automation Suite (Python)
echo   Saudi Building Code (SBC 301 / 304) - CSI ETABS
echo ========================================================
cd /d "%~dp0"
"C:\Users\imrua\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" run_moc.py
if errorlevel 1 (
    echo.
    echo Application exited with an error.
    pause
)
