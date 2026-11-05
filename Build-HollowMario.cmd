@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Build-Release.ps1"
if errorlevel 1 pause
