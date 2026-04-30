@echo off
rem Usage: build-debug.bat 
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration Debug
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration Debug -Version "%~1"
)
