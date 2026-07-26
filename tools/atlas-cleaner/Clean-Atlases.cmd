@echo off
setlocal
set "SCRIPT_DIR=%~dp0"

if "%~1"=="" (
    echo === Scan all character folders ===
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Clean-AllAtlases.ps1"
) else (
    echo === Process: %~nx1 ===
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Clean-Atlas.ps1" -Folder "%~1"
    echo.
    echo Done.
)

pause
