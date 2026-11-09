# make-workshop.ps1
# Build a Steam Workshop-ready folder + .vdf for one mod of this repository.
#
# Only whitelisted files are copied (manifest.xml, the DLLs listed in <Assemblies>,
# Textures/, preview.png). Scripts, archives, source art and .git folders are refused,
# because the in-game scanner checks a Workshop item as a whole.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\make-workshop.ps1 -ModDir auto_uv_lamp
#   powershell -ExecutionPolicy Bypass -File tools\make-workshop.ps1 -ModDir storage_bay_label -Visibility 0
#
# Then upload with steamcmd:
#   D:\SteamCMD\steamcmd.exe +login <account> +workshop_build_item "<repo>\workshop\<id>.vdf" +quit
#
# Keep this file ASCII-only: Windows PowerShell 5.1 misreads UTF-8 without BOM.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ModDir,

    [string]$OutRoot = "",
    [int]$Visibility = 2,
    [string]$Title = "",
    [string]$Description = "",
    [string]$ChangeNote = "Initial release",
    [string]$Preview = ""
)

$ErrorActionPreference = "Stop"

# Resolve the repository root from this script's location (tools\ -> repo root).
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($OutRoot)) { $OutRoot = Join-Path $repoRoot "workshop" }

if (-not (Test-Path -LiteralPath $ModDir)) {
    $candidate = Join-Path $repoRoot $ModDir
    if (Test-Path -LiteralPath $candidate) { $ModDir = $candidate }
    else { throw "mod folder not found: $ModDir" }
}
$ModDir = (Resolve-Path -LiteralPath $ModDir).Path

$manifestPath = Join-Path $ModDir "manifest.xml"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "manifest.xml not found in $ModDir" }

# --- read the manifest with a real XML parser (a malformed one makes the game skip the mod) ---
try { [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 }
catch { throw "manifest.xml is not well-formed XML: $($_.Exception.Message)" }

$modId = $manifest.Manifest.ID
if ([string]::IsNullOrWhiteSpace($modId)) { throw "manifest.xml has no <ID>" }
$modVersion = $manifest.Manifest.ModVersion

$assemblies = @()
if ($manifest.Manifest.Assemblies -and $manifest.Manifest.Assemblies.Assembly) {
    foreach ($a in @($manifest.Manifest.Assemblies.Assembly)) {
        $name = "$a".Trim()
        if ($name) { $assemblies += $name }
    }
}
if ($assemblies.Count -eq 0) { throw "manifest.xml lists no <Assemblies> entry" }

# --- stage a clean folder ---
if (-not (Test-Path -LiteralPath $OutRoot)) { New-Item -ItemType Directory -Path $OutRoot -Force | Out-Null }
$OutRoot = (Resolve-Path -LiteralPath $OutRoot).Path
$dest = Join-Path $OutRoot $modId
if (Test-Path -LiteralPath $dest) {
    # Guarded delete: only a staging folder that sits directly under the staging root.
    $parent = [System.IO.Path]::GetDirectoryName($dest)
    if ($parent.TrimEnd('\') -ne $OutRoot.TrimEnd('\')) {
        throw "refusing to delete a folder outside the staging root: $dest"
    }
    [System.IO.Directory]::Delete($dest, $true)
}
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$dest = (Resolve-Path -LiteralPath $dest).Path

Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $dest "manifest.xml") -Force
Write-Host ("[copy] manifest.xml  (id={0} version={1})" -f $modId, $modVersion)

foreach ($asm in $assemblies) {
    # Prefer the Release build output, fall back to a DLL already sitting next to the manifest.
    $fromBuild = Join-Path $ModDir ("bin\Release\" + $asm)
    $fromRoot  = Join-Path $ModDir $asm
    $src = $null
    if (Test-Path -LiteralPath $fromBuild) { $src = $fromBuild }
    elseif (Test-Path -LiteralPath $fromRoot) { $src = $fromRoot }
    else { throw "assembly not found for '$asm' (looked in bin\Release and the mod root)" }

    Copy-Item -LiteralPath $src -Destination (Join-Path $dest $asm) -Force
    $hash = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
    Write-Host ("[copy] {0}  sha256={1}" -f $asm, $hash.Substring(0, 16).ToLower())
}

$textures = Join-Path $ModDir "Textures"
if (Test-Path -LiteralPath $textures) {
    Copy-Item -LiteralPath $textures -Destination (Join-Path $dest "Textures") -Recurse -Force
    Write-Host "[copy] Textures folder"
}

# --- preview image (< 1 MB per the Workshop rules) ---
if ([string]::IsNullOrWhiteSpace($Preview)) {
    $candidate = Join-Path $ModDir "preview.png"
    if (Test-Path -LiteralPath $candidate) { $Preview = $candidate }
}
$previewOut = ""
if (-not [string]::IsNullOrWhiteSpace($Preview)) {
    if (-not (Test-Path -LiteralPath $Preview)) { throw "preview file not found: $Preview" }
    $sizeMb = (Get-Item -LiteralPath $Preview).Length / 1MB
    if ($sizeMb -ge 1) { throw ("preview file is {0:N2} MB, the limit is < 1 MB" -f $sizeMb) }
    $previewOut = Join-Path $dest "preview.png"
    Copy-Item -LiteralPath $Preview -Destination $previewOut -Force
    Write-Host ("[copy] preview.png  ({0:N0} KB)" -f ($sizeMb * 1024))
}
else {
    Write-Warning "no preview.png: the Workshop item will have no banner unless you pass -Preview or upload one later"
}

# --- refuse anything the scanner would reject ---
$bannedExt = @(".bat", ".cmd", ".ps1", ".js", ".lnk", ".exe", ".zip", ".7z", ".rar", ".psd", ".aseprite", ".pdb")
$allowedTop = @("manifest.xml", "preview.png", "Textures")
$problems = @()

$destFull = (Get-Item -LiteralPath $dest).FullName
Get-ChildItem -LiteralPath $dest -Recurse -Force | ForEach-Object {
    if ($_.PSIsContainer) {
        if ($_.Name -match "^\.(git|vs|idea)$") { $problems += "folder not allowed in a Workshop item: $($_.FullName)" }
        return
    }
    $full = $_.FullName
    if ($full.StartsWith($destFull, [StringComparison]::OrdinalIgnoreCase)) {
        $rel = $full.Substring($destFull.Length).TrimStart('\')
    }
    else { $rel = $_.Name }
    $ext = $_.Extension.ToLower()
    if ($bannedExt -contains $ext) { $problems += "file type not allowed: $rel" }
    $top = $rel.Split('\')[0]
    if ($rel.IndexOf('\') -lt 0) {
        if ($allowedTop -notcontains $rel -and $assemblies -notcontains $rel) { $problems += "unexpected file at item root: $rel" }
    }
}
if ($problems.Count -gt 0) {
    Write-Host ""
    foreach ($p in $problems) { Write-Host ("[refuse] " + $p) -ForegroundColor Red }
    throw "the staged folder contains files the in-game scanner would reject; nothing was published"
}

# --- write the .vdf ---
if ([string]::IsNullOrWhiteSpace($Title))       { $Title = $manifest.Manifest.Name }
if ([string]::IsNullOrWhiteSpace($Description)) { $Description = $manifest.Manifest.Description }
if ([string]::IsNullOrWhiteSpace($Title))       { $Title = $modId }

$existingId = "0"
$vdfPath = Join-Path $OutRoot ($modId + ".vdf")
if (Test-Path -LiteralPath $vdfPath) {
    $m = Select-String -LiteralPath $vdfPath -Pattern '"publishedfileid"\s+"(\d+)"' | Select-Object -First 1
    if ($m -and $m.Matches[0].Groups[1].Value -ne "0") {
        $existingId = $m.Matches[0].Groups[1].Value
        Write-Host ("[vdf] reusing publishedfileid {0} (this run UPDATES the existing item)" -f $existingId)
    }
}

$vdfLines = @(
    '"workshopitem"',
    '{',
    '    "appid"            "4348910"',
    ('    "publishedfileid"  "' + $existingId + '"'),
    ('    "contentfolder"    "' + $dest.Replace('\', '\\') + '"'),
    ('    "previewfile"      "' + $(if ($previewOut) { $previewOut.Replace('\', '\\') } else { "" }) + '"'),
    ('    "visibility"       "' + $Visibility + '"'),
    ('    "title"            "' + $Title.Replace('"', "'") + '"'),
    ('    "description"      "' + $Description.Replace('"', "'") + '"'),
    ('    "changenote"       "' + $ChangeNote.Replace('"', "'") + '"'),
    '}'
)
# Write without a BOM: steamcmd's KeyValues parser does not expect one.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($vdfPath, ($vdfLines -join "`r`n"), $utf8NoBom)

Write-Host ""
Write-Host "staged  : $dest"
Write-Host "vdf     : $vdfPath"
Write-Host "id      : $modId  version $modVersion  visibility $Visibility"
Write-Host ""
Write-Host "upload with:"
Write-Host ('  steamcmd +login <account> +workshop_build_item "' + $vdfPath + '" +quit')
