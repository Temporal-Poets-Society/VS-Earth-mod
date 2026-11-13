param(
    # Cylinder World release archive, or a folder of cylinderworld-<version>.zip files (the newest version is used).
    [string]$CylinderWorld = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Cworld\release')
)
# Builds release\Earth-Regional-Trees-<earth version>.zip: Earth and Cylinder World (both bundled) plus the setup and
# launcher scripts. The external content mods are downloaded by Setup-Earth.cmd from their authors' Mod DB pages.
# Writes the bundled archives' versions and SHA-256 hashes into modpack\pack-lock.json (commit that change with the
# release). Run after `dotnet build -c Release` has produced release\earthmap-<modinfo version>.zip.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path -Parent $PSScriptRoot

function Read-ModInfo([string]$Path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry('modinfo.json')
        if (!$entry) { throw "No modinfo.json in $Path" }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    [pscustomobject]@{
        id = [regex]::Match($json, '(?i)"modid"\s*:\s*"([^"]+)"').Groups[1].Value
        name = [regex]::Match($json, '(?i)"name"\s*:\s*"([^"]+)"').Groups[1].Value
        version = [regex]::Match($json, '(?i)"version"\s*:\s*"([^"]+)"').Groups[1].Value
    }
}
function Bundled([string]$Path) {
    $info = Read-ModInfo $Path
    [ordered]@{ id = $info.id; name = $info.name; version = $info.version; filename = [IO.Path]::GetFileName($Path);
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}

$version = [regex]::Match((Get-Content -LiteralPath "$root\modinfo.json" -Raw), '"version"\s*:\s*"([^"]+)"').Groups[1].Value
$earthZip = Join-Path "$root\release" "earthmap-$version.zip"
if (!(Test-Path -LiteralPath $earthZip)) { throw "Build Earth in Release mode first: $earthZip is missing." }
if (Test-Path -LiteralPath $CylinderWorld -PathType Container) {
    $cw = Get-ChildItem -LiteralPath $CylinderWorld -Filter 'cylinderworld-*.zip' |
        Sort-Object { [version]($_.BaseName -replace '^cylinderworld-', '' -replace '[^0-9.].*$', '') } | Select-Object -Last 1
    if (!$cw) { throw "No cylinderworld-<version>.zip in $CylinderWorld" }
    $CylinderWorld = $cw.FullName
}
if (!(Test-Path -LiteralPath $CylinderWorld -PathType Leaf)) { throw "Cylinder World archive not found: $CylinderWorld (pass -CylinderWorld)." }

$earth = Bundled $earthZip
$cylinder = Bundled $CylinderWorld
if ($earth.id -ne 'earthmap' -or $cylinder.id -ne 'cylinderworld') { throw 'Unexpected bundled mod identities.' }
$lockPath = "$root\modpack\pack-lock.json"
$pack = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
if ($pack.format -ne 2) { throw 'Expected pack lock format 2.' }
$pack.bundled = @($earth, $cylinder)
$pack.created = (Get-Date -Format 'yyyy-MM-dd')
[IO.File]::WriteAllText($lockPath, ($pack | ConvertTo-Json -Depth 10) + "`n", (New-Object Text.UTF8Encoding($false)))

$stage = Join-Path $root "obj\regional-tree-pack-$version"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($f in 'Install-Earth.ps1', 'Start-Earth.ps1', 'Setup-Earth.cmd', 'Start-Earth.cmd', 'README.md', 'pack-lock.json') {
    Copy-Item -LiteralPath "$root\modpack\$f" -Destination $stage
}
Copy-Item -LiteralPath $earthZip, $CylinderWorld -Destination $stage
$destination = "$root\release\Earth-Regional-Trees-$version.zip"
Compress-Archive -Path "$stage\*" -DestinationPath $destination -Force
Write-Host "Built $destination with Earth $($earth.version) and Cylinder World $($cylinder.version); modpack\pack-lock.json updated."
