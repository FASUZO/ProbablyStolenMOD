@echo off
setlocal EnableDelayedExpansion

rem ============================================================
rem  Build + install "Auto UV Lamp" for Probably Stolen
rem  Usage:  build.bat
rem          build.bat "D:\path\to\Probably Stolen"
rem  Requires: .NET SDK 6.0 or newer (dotnet --list-sdks must list one)
rem ============================================================

set "GAME=%~1"
if not "%GAME%"=="" goto :havegame

set "GAME=D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
if exist "%GAME%\Probably Stolen.exe" goto :havegame

set "GAME=D:\SteamLibrary\steamapps\common\Probably Stolen"
if exist "%GAME%\Probably Stolen.exe" goto :havegame

echo [ERROR] Game folder not found. Pass it as the first argument, quoted:
echo         build.bat "D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
goto :end

:havegame
echo Game folder : %GAME%
echo.

echo [1/4] Checking .NET SDK...
for /f "delims=" %%v in ('dotnet --version 2^>nul') do set "DOTNETVER=%%v"
if "%DOTNETVER%"=="" (
    echo [ERROR] No .NET SDK found.
    echo         Install "SDK 8.0" from https://dotnet.microsoft.com/download/dotnet/8.0
    echo         The .NET *Runtime* alone is not enough; the SDK provides "dotnet build".
    goto :end
)
echo         dotnet %DOTNETVER%
echo.

echo [2/4] Checking the game is closed...
tasklist /FI "IMAGENAME eq Probably Stolen.exe" 2>nul | find /I "Probably Stolen.exe" >nul
if not errorlevel 1 (
    echo [ERROR] Probably Stolen.exe is running. Close the game first,
    echo         otherwise the DLL in the Mods folder cannot be replaced.
    goto :end
)
echo         OK
echo.

echo [3/4] Building...
dotnet build -c Release -p:GameDir="%GAME%"
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed - copy the compiler output above and send it back.
    goto :end
)

set "OUT=%~dp0bin\Release\AutoUvLamp.dll"
if not exist "%OUT%" (
    echo [ERROR] Build succeeded but %OUT% is missing.
    goto :end
)
for %%F in ("%OUT%") do set "OUTSIZE=%%~zF"
echo         built AutoUvLamp.dll (!OUTSIZE! bytes)
echo.

echo [4/4] Installing to %GAME%\Mods\auto_uv_lamp ...
set "DEST=%GAME%\Mods\auto_uv_lamp"
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%OUT%" "%DEST%\" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy the DLL. Is the game still running? Is the folder read-only?
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
echo Done. Next:
echo   1. Start the game.
echo   2. Main menu -^> Mods -^> pick the UV faucet mod (folder: auto_uv_lamp) -^> Activate.
echo   3. Restart the game when it asks (activation and restart are two separate steps).
echo   4. Put the UV Lamp (uv_filter) in your backpack - it works from then on.
echo.
echo Faucet: double-click the faucet in game, then drag the UV Lamp from your
echo backpack into the "UV Lamp" slot to install it (drag it out to remove).
echo Tap water is purified only while the lamp is installed.
echo.
echo Log check: create an empty file named dev.txt next to Probably Stolen.exe,
echo then press F8 in game; lines start with [UV Faucet].

:end
echo.
pause
