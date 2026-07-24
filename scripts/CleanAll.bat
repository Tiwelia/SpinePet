@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
if "%~1"=="" (
    echo === Scan all character folders ===
    echo.
    powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%_CleanAll.ps1"
) else (
    echo === Process: %~nx1 ===
    powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%_CleanAtlas.ps1" "%~1"
    echo.
    echo Done.
)
pause
