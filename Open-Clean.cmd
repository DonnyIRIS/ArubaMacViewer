@echo off
rem Removes stale Visual Studio/MSBuild state that causes "load failed", then opens the solution.
cd /d "%~dp0"
rmdir /s /q .vs 2>nul
rmdir /s /q bin 2>nul
rmdir /s /q obj 2>nul
del /q *.bak 2>nul
start "" "ArubaMacViewer.sln"
