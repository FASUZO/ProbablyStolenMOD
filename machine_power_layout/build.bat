@echo off
setlocal EnableDelayedExpansion

rem ============================================================
rem  Build + install the Machine Power Layout mod.
rem  Usage:  build.bat
rem          build.bat "D:\path\to\Probably Stolen"
rem  Requires: .NET SDK 6.0+ (dotnet --list-sdks must list one).
rem  Keep this file ASCII-only: cmd.exe reads it in the OEM code page.
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
echo Mod id      : %MODID%
echo.

echo [1/4] Checking .NET SDK...
for /f "delims=" %%v in ('dotnet --version 2^>nul') do set "DOTNETVER=%%v"
if "%DOTNETVER%"=="" (
    echo [ERROR] No .NET SDK found. Install SDK 8.0 from https://dotnet.microsoft.com/download
    goto :end
)
echo         dotnet %DOTNETVER%
echo.

echo [2/4] Checking the game is closed...
tasklist /FI "IMAGENAME eq Probably Stolen.exe" 2>nul | find /I "Probably Stolen.exe" >nul
if not errorlevel 1 (
    echo [ERROR] Probably Stolen.exe is running. Close the game first.
    goto :end
)
echo         OK
echo.

echo [3/4] Building...
dotnet build -c Release -p:GameDir="%GAME%"
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed - copy the compiler output above.
    goto :end
)

set "OUT=%~dp0bin\Release\%MODDLL%"
if not exist "%OUT%" (
    echo [ERROR] Build succeeded but %OUT% is missing.
    goto :end
)
for %%F in ("%OUT%") do set "OUTSIZE=%%~zF"
echo         built %MODDLL% (!OUTSIZE! bytes)
echo.

echo [4/4] Installing to %GAME%\Mods\%MODID% ...
set "DEST=%GAME%\Mods\%MODID%"
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%OUT%" "%DEST%\" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy the DLL. Is the game still running?
    goto :end
)
copy /y "%~dp0manifest.xml" "%DEST%\" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy manifest.xml.
    goto :end
)

echo.
echo Installed files:
dir /b "%DEST%"
echo.
echo Done. Next: start the game -^> Mods -^> Activate -^> restart the game.
echo In store: press the hotkey (default F7). Log: Player.log

:end
echo.
pause
