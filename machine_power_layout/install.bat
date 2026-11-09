@echo off
setlocal EnableDelayedExpansion

rem ============================================================
rem  Copy the already-built mod into the game's Mods folder.
rem  Use this after closing the game (a running game locks the DLL).
rem  Usage:  install.bat
rem          install.bat "D:\path\to\Probably Stolen"
rem  Keep this file ASCII-only (cmd.exe reads the OEM code page).
rem ============================================================

set "MODID=machine_power_layout"
set "MODDLL=MachinePowerLayout.dll"

set "GAME=%~1"
if not "%GAME%"=="" goto :havegame

set "GAME=D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
if exist "%GAME%\Probably Stolen.exe" goto :havegame

set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
if exist "%GAME%\Probably Stolen.exe" goto :havegame

echo [ERROR] Game folder not found. Pass it as the first argument, quoted.
goto :end

:havegame
echo Game folder : %GAME%

tasklist /FI "IMAGENAME eq Probably Stolen.exe" 2>nul | find /I "Probably Stolen.exe" >nul
if not errorlevel 1 (
    echo [ERROR] Probably Stolen.exe is running. Close the game first, then run this again.
    goto :end
)

set "SRC=%~dp0bin\Release\%MODDLL%"
if not exist "%SRC%" (
    echo [ERROR] %SRC% not found. Run build.bat first.
    goto :end
)

set "DEST=%GAME%\Mods\%MODID%"
if not exist "%DEST%" mkdir "%DEST%"

copy /y "%SRC%" "%DEST%\" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy the DLL. Is the game really closed?
    goto :end
)
copy /y "%~dp0manifest.xml" "%DEST%\" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy manifest.xml.
    goto :end
)

echo.
echo Installed:
dir /b "%DEST%"
echo.
echo Version now installed:
findstr /i "ModVersion" "%DEST%\manifest.xml"
echo.
echo Next: start the game, open Mods, confirm the version above, then press F7 in store.

:end
echo.
pause
