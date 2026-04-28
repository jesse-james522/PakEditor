@echo off
rem Usage: build-release.bat [version]
rem   e.g. build-release.bat 1.0-a2
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration Release
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration Release -Version "%~1"
)
