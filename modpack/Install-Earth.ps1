param(
    [string]$ProfilePath = (Join-Path $PSScriptRoot 'EarthProfile'),
    [string]$CachePath = (Join-Path $PSScriptRoot 'Downloads'),
    [switch]$Offline,
    # Testing only: take "downloads" from this local folder instead of the Mod DB CDN.
    [string]$MirrorPath
)
# Windows PowerShell 5.1 compatible. Every archive is verified (SHA-256 and modinfo identity) before it is used and is
# moved into place with a rename, so an interrupted run never leaves a partial file under its final name. A damaged
# cached download is deleted and downloaded again; a damaged archive in the profile is replaced.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$lockPath = Join-Path $PSScriptRoot 'pack-lock.json'
$pack = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
if ($pack.format -ne 2 -or !$pack.bundled -or !$pack.mods) { throw 'Unsupported or incomplete pack lock (format 2 with bundled and downloaded mods expected).' }
$entries = @()
foreach ($e in @($pack.bundled)) { $entries += [pscustomobject]@{ Entry = $e; Bundled = $true } }
foreach ($e in @($pack.mods)) { $entries += [pscustomobject]@{ Entry = $e; Bundled = $false } }
foreach ($item in $entries) {
    $entry = $item.Entry
    if ($entry.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw "Pack lock is not finalized for $($entry.id): run tools/build-regional-tree-pack.ps1 to record the archive hashes." }
    if ($entry.filename -ne [IO.Path]::GetFileName($entry.filename) -or $entry.filename -notmatch '^[a-zA-Z0-9._-]+\.zip$' -or $entry.id -notmatch '^[a-z0-9_-]+$') { throw 'Invalid pack entry.' }
}
$lockHash = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
$ProfilePath = [IO.Path]::GetFullPath($ProfilePath)
$CachePath = [IO.Path]::GetFullPath($CachePath)
$modsPath = Join-Path $ProfilePath 'Mods'
$marker = Join-Path $ProfilePath '.earth-pack.json'
if (Test-Path -LiteralPath $ProfilePath) {
    if (!(Test-Path -LiteralPath $marker)) { throw "Use a new folder for the Earth profile: $ProfilePath" }
    $previous = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($previous.lockHash -ne $lockHash) { throw 'This profile belongs to a different pack release. Choose a new profile folder.' }
}
New-Item -ItemType Directory -Path $ProfilePath, $modsPath, (Join-Path $ProfilePath 'ModConfig'), $CachePath -Force | Out-Null
if (!(Test-Path -LiteralPath $marker)) {
    @{pack=$pack.name;lockHash=$lockHash;gameVersion=$pack.gameVersion;complete=$false} | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
}
# Leftovers of an interrupted run (never loaded by the game: not .zip).
Get-ChildItem -LiteralPath $modsPath -Filter '*.partial' -File | Remove-Item -Force
Get-ChildItem -LiteralPath $CachePath -Filter '*.download' -File | Remove-Item -Force

# Returns $null for a good archive, otherwise the reason.
function Test-Archive([string]$Path, $Entry) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Entry.sha256.ToUpperInvariant()) { return 'checksum mismatch' }
    $zip = $null
    try {
        $zip = [IO.Compression.ZipFile]::OpenRead($Path)
        $info = $zip.GetEntry('modinfo.json')
        if (!$info) { return 'no modinfo.json' }
        $reader = New-Object IO.StreamReader($info.Open())
        try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() }
        # VS accepts JSON with trailing commas; Windows PowerShell's JSON parser does not.
        # The archive is already authenticated by its pinned hash. Read only the two string fields.
        $modId = [regex]::Match($json, '(?i)"modid"\s*:\s*"([^"]+)"').Groups[1].Value
        $version = [regex]::Match($json, '(?i)"version"\s*:\s*"([^"]+)"').Groups[1].Value
        if ($modId -ne $Entry.id -or $version -ne $Entry.version) { return "mod identity $modId $version differs from the pack lock" }
    } catch {
        return 'unreadable archive'
    } finally {
        if ($zip) { $zip.Dispose() }
    }
    return $null
}

# Copies to a temporary name next to the target, verifies, then renames into place.
function Install-Archive([string]$Source, [string]$Target, $Entry) {
    $partial = $Target + '.partial'
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    Copy-Item -LiteralPath $Source -Destination $partial
    $problem = Test-Archive $partial $Entry
    if ($problem) { Remove-Item -LiteralPath $partial -Force; throw "Copy of $($Entry.filename) failed verification ($problem). Nothing was replaced." }
    if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Force }
    Move-Item -LiteralPath $partial -Destination $Target
}

function Get-Download($Entry, [string]$Destination) {
    $temporary = Join-Path $CachePath ([Guid]::NewGuid().ToString() + '.download')
    try {
        if ($MirrorPath) {
            $source = Join-Path $MirrorPath $Entry.filename
            if (!(Test-Path -LiteralPath $source)) { throw "Mirror has no $($Entry.filename)" }
            Copy-Item -LiteralPath $source -Destination $temporary
        } else {
            $uri = [Uri]$Entry.url
            if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'moddbcdn.vintagestory.at') { throw "Unexpected download source: $uri" }
            Write-Host "Downloading $($Entry.name) $($Entry.version)..."
            Invoke-WebRequest -Uri $uri -OutFile $temporary -UseBasicParsing
        }
        $problem = Test-Archive $temporary $Entry
        if ($problem) { throw "Download of $($Entry.filename) failed verification ($problem). Nothing was installed; run setup again later." }
        if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
        Move-Item -LiteralPath $temporary -Destination $Destination
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

foreach ($item in $entries) {
    $entry = $item.Entry
    $target = Join-Path $modsPath $entry.filename
    if (Test-Path -LiteralPath $target) {
        $problem = Test-Archive $target $entry
        if (!$problem) { continue }
        Write-Warning "Replacing $($entry.filename) in the profile ($problem)."
    }
    if ($item.Bundled) {
        $archive = Join-Path $PSScriptRoot $entry.filename
        if (!(Test-Path -LiteralPath $archive)) { throw "Missing archive: $archive. Extract the complete pack." }
        $problem = Test-Archive $archive $entry
        if ($problem) { throw "The pack's own $($entry.filename) is damaged ($problem). Extract the pack again." }
    } else {
        $archive = Join-Path $CachePath $entry.filename
        if (Test-Path -LiteralPath $archive) {
            $problem = Test-Archive $archive $entry
            if ($problem) {
                Write-Warning "Discarding the damaged cached download $($entry.filename) ($problem)."
                Remove-Item -LiteralPath $archive -Force
            }
        }
        if (!(Test-Path -LiteralPath $archive)) {
            if ($Offline -and !$MirrorPath) { throw "Missing archive: $archive (offline mode)." }
            Get-Download $entry $archive
        }
    }
    Install-Archive $archive $target $entry
}
$config = Join-Path $ProfilePath 'ModConfig\earthmap.json'
if (!(Test-Path -LiteralPath $config)) {
    @{RegionalTrees=$true;UpgradeExistingWorldTreeRules=$false} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding UTF8
}
Write-Host "Earth Regional Trees is ready: $ProfilePath"
@{pack=$pack.name;lockHash=$lockHash;gameVersion=$pack.gameVersion;complete=$true} | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
Write-Host "Use Vintage Story $($pack.gameVersion). Run Start-Earth.cmd and create a new world."
Write-Host 'Existing profiles, saves and mod configurations were preserved.'
