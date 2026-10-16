<#
.SYNOPSIS
    Bascule SteamVR entre le mode « sans casque » (Vive Trackers seuls, pilote null) et le mode
    « avec casque » (PICO 4 Enterprise via Business Streaming, ou tout autre casque réel).

.DESCRIPTION
    Procédure de https://github.com/username223/SteamVRNoHeadset, dans les deux sens, plus la
    configuration kiosque du poste de production. Voir tools/steamvr/README.md.

    ⚠ Applique EXACTEMENT les mêmes règles que le jeu (Assets/Mooveo/Scripts/Hardware/SteamVRConfig.cs) :
    toute modification de l'un doit être reportée dans l'autre.

    - Le fichier utilisateur est celui que déclare %LOCALAPPDATA%\openvr\openvrpaths.vrpath (dossier
      « config »), PAS forcément Steam\config\steamvr.vrsettings : depuis PICO Connect, SteamVR lit
      %LOCALAPPDATA%\openvr\config, et l'ancien fichier n'est plus lu.
    - Sauvegarde les fichiers touchés AVANT toute écriture (tools/steamvr/backups/<horodatage>/).
    - Refuse de tourner si SteamVR est ouvert : il réécrit son fichier de réglages en quittant et
      écraserait la modification.
    - Ne touche QUE les clés listées ci-dessous. Le reste des fichiers est conservé tel quel.

.PARAMETER Mode
    NoHeadset : pilote null forcé, casque non requis (production, Vive Trackers).
    Headset   : casque réel requis (PICO 4 Enterprise).

.PARAMETER Status
    Affiche l'état courant sans rien modifier.

.EXAMPLE
    .\tools\steamvr\Set-SteamVRProfile.ps1 -Status
    .\tools\steamvr\Set-SteamVRProfile.ps1 -Mode Headset
    .\tools\steamvr\Set-SteamVRProfile.ps1 -Mode NoHeadset
#>
[CmdletBinding(DefaultParameterSetName = 'Switch')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Switch')]
    [ValidateSet('NoHeadset', 'Headset')]
    [string] $Mode,

    [Parameter(Mandatory, ParameterSetName = 'Status')]
    [switch] $Status,

    [string] $SteamPath
)

$ErrorActionPreference = 'Stop'

# --- Localisation (même règle que SteamVRConfig.TryLocate) -----------------------------------

if (-not $SteamPath) {
    $SteamPath = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath
    if (-not $SteamPath) { $SteamPath = 'C:\Program Files (x86)\Steam' }
}

$steamVrDir = $null
$configDirs = @()

# 1. Registre d'OpenVR : dossier de réglages ET dossier de SteamVR, quelle que soit la bibliothèque.
$vrpath = Join-Path $env:LOCALAPPDATA 'openvr\openvrpaths.vrpath'
if (Test-Path $vrpath) {
    try {
        $reg = [IO.File]::ReadAllText($vrpath) | ConvertFrom-Json
        if ($reg.config) { $configDirs += @($reg.config) }
        foreach ($dir in @($reg.runtime)) {
            if ($dir -and (Test-Path $dir)) { $steamVrDir = $dir; break }
        }
    } catch {
        Write-Warning "openvrpaths.vrpath illisible, repli sur les bibliothèques Steam : $($_.Exception.Message)"
    }
}

# 2. Repli : bibliothèques Steam.
if (-not $steamVrDir) {
    $libraries = @($SteamPath)
    $vdf = Join-Path $SteamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libraries += Get-Content $vdf | Select-String '"path"' | ForEach-Object { ($_.Line -split '"')[3].Replace('\\', '\') }
    }
    foreach ($lib in $libraries) {
        $candidate = Join-Path $lib 'steamapps\common\SteamVR'
        if (Test-Path $candidate) { $steamVrDir = $candidate; break }
    }
}
if (-not $steamVrDir) { throw "SteamVR introuvable (ni openvrpaths.vrpath, ni bibliothèques Steam)." }

# Dossier de réglages : le premier qui porte déjà steamvr.vrsettings, sinon le premier déclaré,
# sinon Steam\config (steamapps\common\SteamVR -> 3 niveaux au-dessus).
$legacyConfig = [IO.Path]::GetFullPath((Join-Path $steamVrDir '..\..\..\config'))
$configDirs += $legacyConfig
$configDir = ($configDirs | Where-Object { Test-Path (Join-Path $_ 'steamvr.vrsettings') } | Select-Object -First 1)
if (-not $configDir) { $configDir = $configDirs[0] }

$userSettings   = Join-Path $configDir 'steamvr.vrsettings'
$legacySettings = Join-Path $legacyConfig 'steamvr.vrsettings'
# Configuration de pièce. Sans l'univers du casque factice, SteamVR lance l'assistant de configuration
# de la pièce à chaque démarrage en mode trackers, et cet assistant fait QUITTER GraffWall.
$chaperone       = Join-Path $configDir 'chaperone_info.vrchap'
$legacyChaperone = Join-Path $legacyConfig 'chaperone_info.vrchap'
$nullUniverse    = '2'   # = SteamVRConfig.NullDriverUniverseId
$nullDefaults   = Join-Path $steamVrDir 'drivers\null\resources\settings\default.vrsettings'
$vrDefaults     = Join-Path $steamVrDir 'resources\settings\default.vrsettings'

# --- Valeurs attendues (même liste que SteamVRConfig.ExpectedUserSettings) --------------------
# $null = pas de réglage propre : la clé est retirée, SteamVR reprend sa valeur par défaut.

function Get-ExpectedUser([bool] $noHeadset) {
    $list = @(
        @('steamvr',     'requireHmd',              (-not $noHeadset)),
        @('steamvr',     'forcedDriver',            $(if ($noHeadset) { 'null' } else { '' })),
        @('steamvr',     'activateMultipleDrivers', $noHeadset),
        @('driver_null', 'enable',                  $noHeadset)
    )
    if ($noHeadset) {
        $list += ,@('driver_null', 'renderWidth',               16)
        $list += ,@('driver_null', 'renderHeight',              16)
        $list += ,@('driver_null', 'displayFrequency',          60)
        $list += ,@('steamvr',     'motionSmoothing',           $false)
        $list += ,@('steamvr',     'supersampleManualOverride', $true)
        $list += ,@('steamvr',     'supersampleScale',          0.5)
        $list += ,@('steamvr',     'hasSeenRoomSetup',          $true)
    } else {
        $list += ,@('steamvr', 'motionSmoothing',           $null)
        $list += ,@('steamvr', 'supersampleManualOverride', $null)
        $list += ,@('steamvr', 'supersampleScale',          $null)
    }
    return ,$list
}

# Les trois clés de SteamVRNoHeadset dans les défauts de SteamVR (valeurs d'origine en mode casque).
function Get-ExpectedDefaults([bool] $noHeadset) {
    return ,@(
        @('requireHmd',              (-not $noHeadset)),
        @('forcedDriver',            $(if ($noHeadset) { 'null' } else { '' })),
        @('activateMultipleDrivers', $noHeadset)
    )
}

# --- Lecture ----------------------------------------------------------------------------------

function Read-Json([string] $path) {
    if (-not (Test-Path $path)) { return [pscustomobject]@{} }
    $text = [IO.File]::ReadAllText($path)
    if ([string]::IsNullOrWhiteSpace($text)) { return [pscustomobject]@{} }
    return $text | ConvertFrom-Json
}

function Get-Value($obj, [string] $section, [string] $key) {
    $s = $obj.PSObject.Properties[$section]
    if (-not $s) { return $null }
    $k = $s.Value.PSObject.Properties[$key]
    if (-not $k) { return $null }
    return $k.Value
}

function Format-Value($v) {
    if ($null -eq $v) { return '(absent)' }
    if ($v -is [bool]) { return $v.ToString().ToLower() }
    if ($v -is [string]) { return "`"$v`"" }
    return [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $v)
}

function Show-State {
    $u = Read-Json $userSettings
    $n = Read-Json $nullDefaults
    $d = Read-Json $vrDefaults

    $forced = Get-Value $u 'steamvr' 'forcedDriver'
    if ($null -eq $forced) { $forced = Get-Value $d 'steamvr' 'forcedDriver' }
    $noHeadset = ($forced -eq 'null')

    Write-Host ''
    Write-Host "Profil effectif : $(if ($noHeadset) { 'NoHeadset (pilote null, trackers seuls)' } else { 'Headset (casque réel)' })" -ForegroundColor Cyan
    Write-Host "  Fichier utilisateur (fait foi) : $userSettings"
    foreach ($e in (Get-ExpectedUser $true) + (Get-ExpectedUser $false) | Sort-Object { "$($_[0]).$($_[1])" } -Unique) {
        $v = Get-Value $u $e[0] $e[1]
        $fallback = if ($e[0] -eq 'driver_null') { Get-Value $n $e[0] $e[1] } else { $null }
        if ($null -eq $fallback) { $fallback = Get-Value $d $e[0] $e[1] }
        $shown = if ($null -ne $v) { Format-Value $v } else { "(absent, défaut $(Format-Value $fallback))" }
        Write-Host ("    {0,-36} = {1}" -f "$($e[0]).$($e[1])", $shown)
    }
    Write-Host "  $nullDefaults"
    Write-Host "    driver_null.enable                   = $(Format-Value (Get-Value $n 'driver_null' 'enable'))"
    Write-Host "  $vrDefaults (réécrit à chaque mise à jour de SteamVR)"
    foreach ($e in (Get-ExpectedDefaults $true)) {
        Write-Host ("    {0,-36} = {1}" -f "steamvr.$($e[0])", (Format-Value (Get-Value $d 'steamvr' $e[0])))
    }
    $universes = @()
    if (Test-Path $chaperone) {
        try { $universes = @((Read-Json $chaperone).universes | ForEach-Object { $_.universeID }) } catch { $universes = @('(illisible)') }
    }
    Write-Host "  $chaperone"
    Write-Host "    univers                              = $(if ($universes) { $universes -join ', ' } else { '(aucun)' }) (casque factice : $nullUniverse)"
    if ((Test-Path $legacySettings) -and ($legacySettings -ne $userSettings)) {
        Write-Host ''
        Write-Host "  ⚠ $legacySettings existe mais n'est PLUS LU par SteamVR (ancien emplacement)." -ForegroundColor Yellow
    }
    Write-Host ''
}

if ($Status) { Show-State; return }

# --- Garde-fous -------------------------------------------------------------------------------

# Mêmes processus que SteamVRConfig.ProcessNames : la fenêtre de paramètres (vrwebhelper) comprise.
$names = 'vrmonitor', 'vrserver', 'vrcompositor', 'vrdashboard', 'vrwebhelper', 'vrstartup', 'vrserverhelper'
$running = Get-Process $names -ErrorAction SilentlyContinue
if ($running) {
    throw "SteamVR est ouvert ($(($running.Name | Sort-Object -Unique) -join ', ')). Fermez-le d'abord : il réécrit ses réglages en quittant."
}

# --- Sauvegarde -------------------------------------------------------------------------------

$backupDir = Join-Path $PSScriptRoot ("backups\" + (Get-Date -Format 'yyyy-MM-dd_HH-mm-ss') + "_avant_$Mode")
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
if (Test-Path $userSettings) { Copy-Item $userSettings (Join-Path $backupDir 'steamvr.vrsettings') }
if (Test-Path $nullDefaults) { Copy-Item $nullDefaults (Join-Path $backupDir 'null_driver_default.vrsettings') }
if (Test-Path $vrDefaults)   { Copy-Item $vrDefaults   (Join-Path $backupDir 'steamvr_default.vrsettings') }
if (Test-Path $chaperone)    { Copy-Item $chaperone    (Join-Path $backupDir 'chaperone_info.vrchap') }
@"
Sauvegarde faite avant bascule vers le profil $Mode.
Restauration manuelle (SteamVR fermé) :
  copy steamvr.vrsettings             "$userSettings"
  copy null_driver_default.vrsettings "$nullDefaults"
  copy steamvr_default.vrsettings     "$vrDefaults"
  copy chaperone_info.vrchap          "$chaperone"
"@ | Set-Content -Encoding UTF8 (Join-Path $backupDir 'LISEZMOI.txt')
Write-Host "Sauvegarde : $backupDir" -ForegroundColor DarkGray

# --- Écriture ---------------------------------------------------------------------------------

function Set-Value($obj, [string] $section, [string] $key, $value) {
    if (-not $obj.PSObject.Properties[$section]) {
        $obj | Add-Member -NotePropertyName $section -NotePropertyValue ([pscustomobject]@{})
    }
    $s = $obj.$section
    if ($null -eq $value) {
        if ($s.PSObject.Properties[$key]) { $s.PSObject.Properties.Remove($key) }
        return
    }
    if ($s.PSObject.Properties[$key]) { $s.$key = $value }
    else { $s | Add-Member -NotePropertyName $key -NotePropertyValue $value }
}

# ⚠ UTF-8 SANS BOM : le lecteur JSON de SteamVR n'est pas garanti de l'accepter.
function Write-Utf8NoBom([string] $path, [string] $text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}

# Remplace la valeur d'une clé EN PLACE (première occurrence), le reste du fichier de Valve intact.
function Set-InPlace([string] $text, [string] $key, $value) {
    $json = if ($value -is [bool]) { $value.ToString().ToLower() } else { "`"$value`"" }
    $rx = New-Object Text.RegularExpressions.Regex ('("' + [regex]::Escape($key) + '"\s*:\s*)(true|false|"[^"]*")')
    return $rx.Replace($text, { param($m) $m.Groups[1].Value + $json }, 1)
}

$noHeadset = ($Mode -eq 'NoHeadset')

# 1. Fichier utilisateur : c'est LUI qui fait foi.
$u = Read-Json $userSettings
foreach ($e in (Get-ExpectedUser $noHeadset)) { Set-Value $u $e[0] $e[1] $e[2] }
$json = $u | ConvertTo-Json -Depth 20
# Vérifié avant écriture : un fichier illisible ferait repartir SteamVR sur ses valeurs par défaut.
$null = $json | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path (Split-Path $userSettings) | Out-Null
Write-Utf8NoBom $userSettings $json

# 2. Défauts du pilote null.
if (Test-Path $nullDefaults) {
    $nullText = [IO.File]::ReadAllText($nullDefaults)
    $nullText = [regex]::Replace($nullText, '("enable"\s*:\s*)(true|false)', "`${1}$($noHeadset.ToString().ToLower())", 1)
    Write-Utf8NoBom $nullDefaults $nullText
}

# 3. Défauts de SteamVR (procédure SteamVRNoHeadset) : ils ne décident de rien face au fichier
#    utilisateur, mais SteamVR RETIRE du fichier utilisateur toute clé égale à ces défauts. Ils doivent
#    donc rester cohérents avec le mode. Échec non bloquant.
if (Test-Path $vrDefaults) {
    try {
        $text = [IO.File]::ReadAllText($vrDefaults)
        $new = $text
        foreach ($e in (Get-ExpectedDefaults $noHeadset)) { $new = Set-InPlace $new $e[0] $e[1] }
        if ($new -ne $text) {
            $null = $new | ConvertFrom-Json
            Write-Utf8NoBom $vrDefaults $new
        }
    } catch {
        Write-Warning "Défauts de SteamVR non modifiés ($vrDefaults) : $($_.Exception.Message). Sans conséquence tant que le fichier utilisateur porte les réglages."
    }
}

# 4. Mode trackers : univers du casque factice dans la configuration de pièce, AJOUTÉ sans toucher
#    aux autres (celui du PICO). Repris de l'ancienne configuration de production, sinon pièce neutre.
#    Un fichier illisible n'est jamais écrasé : il porte les univers des autres casques.
if ($noHeadset) {
    try {
        $c = if (Test-Path $chaperone) { Read-Json $chaperone } else { [pscustomobject]@{ jsonid = 'chaperone_info'; universes = @(); version = 5 } }
        $existing = @($c.universes | Where-Object { $_.universeID -eq $nullUniverse })
        if (-not $existing) {
            $entry = $null
            if (Test-Path $legacyChaperone) {
                try { $entry = @((Read-Json $legacyChaperone).universes | Where-Object { $_.universeID -eq $nullUniverse })[0] } catch { }
            }
            if (-not $entry) {
                $x = 1.5; $z = 1.125; $h = 2.43
                $pts = @(@(-$x, -$z), @($x, -$z), @($x, $z), @(-$x, $z))
                $walls = @()
                for ($i = 0; $i -lt 4; $i++) {
                    $a = $pts[$i]; $b = $pts[($i + 1) % 4]
                    $walls += ,@(@($a[0], 0, $a[1]), @($a[0], $h, $a[1]), @($b[0], $h, $b[1]), @($b[0], 0, $b[1]))
                }
                $entry = [pscustomobject]@{
                    collision_bounds = $walls
                    play_area        = @(3, 2.25)
                    seated           = [pscustomobject]@{ translation = @(0, 1.2, 0); yaw = 0 }
                    standing         = [pscustomobject]@{ translation = @(0, 0, 0); yaw = 0 }
                    time             = (Get-Date).ToString('ddd MMM dd HH:mm:ss yyyy', [Globalization.CultureInfo]::InvariantCulture)
                    universeID       = $nullUniverse
                }
            }
            $c.universes = @($c.universes) + $entry
            $cjson = $c | ConvertTo-Json -Depth 20
            $null = $cjson | ConvertFrom-Json
            Write-Utf8NoBom $chaperone $cjson
        }
    } catch {
        Write-Warning "Configuration de pièce non modifiée ($chaperone) : $($_.Exception.Message). L'assistant de configuration de la pièce de SteamVR risque de s'ouvrir et de fermer GraffWall."
    }
}

Write-Host "Profil $Mode appliqué." -ForegroundColor Green
Show-State
