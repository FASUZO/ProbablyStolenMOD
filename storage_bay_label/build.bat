@echo off
setlocal EnableDelayedExpansion

rem ============================================================
rem  Build + install the StorageBayLabel mod.
rem  Usage:  build.bat
rem          build.bat "D:\path\to\Probably Stolen"
rem  Requires: .NET SDK (dotnet --list-sdks must list one).
rem  Keep this file ASCII-only: cmd.exe reads it in the OEM code page.
rem ============================================================

set "MODID=storage_bay_label"
set "MODDLL=StorageBayLabel.dll"

set "GAME=%~1"
if not "%GAME%"=="" goto :havegame

set "GAME=D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
if exist "%GAME%\Probably Stolen.exe" goto :havegame

set "GAME=D:\SteamLibrary\steamapps\common\Probably Stolen"
if exist "%GAME%\Probably Stolen.exe" goto :havegame

echo [ERROR] Game folder not found. Pass it as the first argument, quoted.
goto :end

:havegame
echo Game folder : %GAME%
echo Mod id      : %MODID%
echo.

echo [1/5] Checking .NET SDK...
for /f "delims=" %%v in ('dotnet --version 2^>nul') do set "DOTNETVER=%%v"
if "%DOTNETVER%"=="" (
    echo [ERROR] No .NET SDK found.
    goto :end
)
echo         dotnet %DOTNETVER%
echo.

rem A malformed manifest does not fail the build - the game silently skips the
rem whole mod ("Failed to parse manifest.xml ... skipping") and every patch
rem disappears. Always gate on a real XML parse before installing.
echo [2/5] Validating manifest.xml...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { $d = New-Object System.Xml.XmlDocument; $d.Load('%~dp0manifest.xml'); exit 0 } catch { Write-Host ''; Write-Host '[ERROR] manifest.xml is not well-formed XML.'; Write-Host '        The game will SKIP the entire mod and no patch will load.'; Write-Host ('        ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 goto :end
echo         OK
echo.

echo [3/5] Checking the game is closed...
tasklist /FI "IMAGENAME eq Probably Stolen.exe" 2>nul | find /I "Probably Stolen.exe" >nul
if not errorlevel 1 (
    echo [ERROR] Probably Stolen.exe is running. Close the game first.
    goto :end
)
echo         OK
echo.

echo [4/5] Building...
dotnet build -c Release -p:GameDir="%GAME%"
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed - copy the compiler output above and send it back.
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

echo [5/5] Installing to %GAME%\Mods\%MODID% ...
set "DEST=%GAME%\Mods\%MODID%"
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%OUT%" "%DEST%\" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy the DLL.
    goto :end
)
copy /y "%~dp0manifest.xml" "%DEST%\" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy manifest.xml.
    goto :end
)
if exist "%~dp0Textures" (
    xcopy /E /I /Y "%~dp0Textures" "%DEST%\Textures\" >nul
    if errorlevel 1 (
        echo [ERROR] Could not copy the Textures folder.
        goto :end
    )
)

echo.
echo Installed files:
dir /b "%DEST%"
echo.
echo Done. Next: start the game, Mods -^> Activate, then RESTART the game.
echo Log: %%USERPROFILE%%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log

:end
echo.
pause
