# Dump the exact API surface this mod depends on, straight from the installed game.
# Usage: powershell -ExecutionPolicy Bypass -File dump_api.ps1
$ErrorActionPreference = 'Continue'

$dir = 'D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed'
if (-not (Test-Path $dir)) { Write-Output "Managed dir not found: $dir"; exit 1 }

$resolver = {
    param($sender, $eventArgs)
    try {
        $simple = (New-Object System.Reflection.AssemblyName($eventArgs.Name)).Name
        $candidate = Join-Path $dir ($simple + '.dll')
        if (Test-Path $candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate) }
    } catch { }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

try {
    $asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'Assembly-CSharp.dll'))
} catch {
    Write-Output ("LOAD FAILED: " + $_.Exception.Message)
    exit 1
}
Write-Output ("Assembly: " + $asm.FullName)

function Show-Type([string]$name) {
    $t = $asm.GetType($name, $false)
    if ($null -eq $t) { Write-Output ("--- " + $name + ": NOT FOUND"); return }
    Write-Output ("--- " + $t.FullName + "  (base: " + $(if ($t.BaseType) { $t.BaseType.Name } else { 'n/a' }) + ")")
}

function Show-Events([string]$name) {
    Show-Type $name
    $t = $asm.GetType($name, $false)
    if ($null -eq $t) { return }
    $t.GetEvents('Public,Static,Instance,DeclaredOnly') | Sort-Object Name | ForEach-Object {
        Write-Output ("  event " + $_.Name + " : " + $_.EventHandlerType.Name)
    }
}

function Show-Methods([string]$name, [string]$filter) {
    Show-Type $name
    $t = $asm.GetType($name, $false)
    if ($null -eq $t) { return }
    $t.GetMethods('Public,NonPublic,Static,Instance,DeclaredOnly') |
        Where-Object { $filter -eq '' -or $_.Name -match $filter } |
        Sort-Object Name | ForEach-Object {
            $ps = ($_.GetParameters() | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
            Write-Output ("  " + $(if ($_.IsStatic) { 'static ' } else { '' }) + $_.ReturnType.Name + ' ' + $_.Name + '(' + $ps + ')')
        }
}

function Show-Fields([string]$name, [string]$filter) {
    Show-Type $name
    $t = $asm.GetType($name, $false)
    if ($null -eq $t) { return }
    $t.GetFields('Public,NonPublic,Static,Instance,DeclaredOnly') |
        Where-Object { $filter -eq '' -or $_.Name -match $filter } |
        Sort-Object Name | ForEach-Object {
            Write-Output ("  " + $(if ($_.IsStatic) { 'static ' } else { '' }) + $_.FieldType.Name + ' ' + $_.Name)
        }
}

Write-Output ''
Write-Output '===== IMod / lifecycle ====='
Show-Methods 'IMod' ''

Write-Output ''
Write-Output '===== ModHook events ====='
Show-Events 'ModHook'

Write-Output ''
Write-Output '===== ModConfig ====='
Show-Methods 'ModConfig' 'Define|For'
Show-Methods 'ConfigEntry`1' ''

Write-Output ''
Write-Output '===== ModLog ====='
Show-Methods 'ModLog' ''

Write-Output ''
Write-Output '===== ModManifest ====='
Show-Fields 'ModManifest' ''

Write-Output ''
Write-Output '===== WaterHelper ====='
Show-Methods 'WaterHelper' 'TransferLiquid|HandleFilter|GetWaterPurity|GetFreeCapacity|GetTotalVolume|IsDrain|IsFaucet|PurifyToBaseWater|Point|DirectToWater'

Write-Output ''
Write-Output '===== EmporiumEntry ====='
Show-Methods 'EmporiumEntry' 'GetAllOwnedItems|get_Instance'

Write-Output ''
Write-Output '===== GameItem ====='
Show-Methods 'GameItem' '^ModifyTag$|^IsTag$|^GetTagReadonly$|^IsGameItemType$'

Write-Output ''
Write-Output '===== TagState (ModifyTag state) ====='
Show-Methods 'TagState' 'Enable|SetInt|Disable'

Write-Output ''
Write-Output '===== Liquid ====='
Show-Fields 'Liquid' 'Liquids'
Show-Methods 'Liquid' 'GetCurrentPartFromId'
Show-Fields 'Liquid' 'currentPart|identifier|displayName'

Write-Output ''
Write-Output '===== WineHelper / GeneralHelper ====='
Show-Methods 'WineHelper' 'CanModifyLiquid'
Show-Methods 'GeneralHelper' 'IsItemOwned'

Write-Output ''
Write-Output '===== game version ====='
$asm.GetTypes() | Where-Object { $_.Name -match 'GameVersion|BuildInfo' } | ForEach-Object {
    Write-Output ("  type " + $_.FullName)
    $_.GetFields('Public,NonPublic,Static,DeclaredOnly') | ForEach-Object { Write-Output ("    " + $_.FieldType.Name + ' ' + $_.Name) }
    $_.GetProperties('Public,NonPublic,Static,DeclaredOnly') | ForEach-Object { Write-Output ("    prop " + $_.PropertyType.Name + ' ' + $_.Name) }
}

Write-Output ''
Write-Output '===== done ====='
