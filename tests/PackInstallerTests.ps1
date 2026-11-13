param([string]$WorkPath)
# Offline installer tests with small generated archives (no network, no real profile). Runs modpack\Install-Earth.ps1
# under Windows PowerShell 5.1 against a generated pack lock; "downloads" come from a local mirror folder.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$root = Split-Path -Parent $PSScriptRoot
if (!$WorkPath) { $WorkPath = Join-Path $root ('testruns\pack-tests-' + [Guid]::NewGuid().ToString('N')) }
$run = [IO.Path]::GetFullPath($WorkPath)
New-Item -ItemType Directory -Path $run | Out-Null
$checks = 0
function Check([bool]$Ok, [string]$Message) { if (!$Ok) { throw "FAIL: $Message (evidence: $run)" }; $script:checks++ }

function New-ModZip([string]$Path, [string]$Id, [string]$Version) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create)
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $writer = New-Object IO.StreamWriter($zip.CreateEntry('modinfo.json').Open())
        # trailing comma as VS allows
        $writer.Write("{ `"type`": `"content`", `"modid`": `"$Id`", `"version`": `"$Version`", `"name`": `"$Id`", }")
        $writer.Dispose()
        $writer = New-Object IO.StreamWriter($zip.CreateEntry('assets/payload.txt').Open())
        $writer.Write(('payload ' + $Id) * 200)
        $writer.Dispose()
    } finally { $zip.Dispose(); $stream.Dispose() }
}
function Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

# A pack folder: installer + lock + two bundled archives; a mirror holding the two "downloaded" archives.
$packDir = Join-Path $run 'pack'
$mirror = Join-Path $run 'mirror'
New-Item -ItemType Directory -Path $packDir, $mirror | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'modpack\Install-Earth.ps1') -Destination $packDir
New-ModZip "$packDir\earthmap-9.9.9.zip" 'earthmap' '9.9.9'
New-ModZip "$packDir\cylinderworld-1.0.0.zip" 'cylinderworld' '1.0.0'
New-ModZip "$mirror\modone-1.0.0.zip" 'modone' '1.0.0'
New-ModZip "$mirror\modtwo-2.0.0.zip" 'modtwo' '2.0.0'
function Entry([string]$Dir, [string]$Id, [string]$Version, [bool]$Download) {
    $file = "$Id-$Version.zip"
    $e = [ordered]@{ id = $Id; name = $Id; version = $Version; filename = $file; sha256 = (Sha "$Dir\$file") }
    if ($Download) { $e.url = "https://moddbcdn.vintagestory.at/$file" }
    $e
}
$lock = [ordered]@{ format = 2; name = 'Earth test pack'; gameVersion = '1.22.7'; created = '2026-10-02'
    bundled = @((Entry $packDir 'earthmap' '9.9.9' $false), (Entry $packDir 'cylinderworld' '1.0.0' $false))
    mods = @((Entry $mirror 'modone' '1.0.0' $true), (Entry $mirror 'modtwo' '2.0.0' $true)) }
$lockJson = $lock | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText("$packDir\pack-lock.json", $lockJson)
$installer = "$packDir\Install-Earth.ps1"

function Run-Installer([string]$Profile, [string]$Cache, [bool]$ShouldPass, [switch]$UseMirror) {
    $stdout = Join-Path $run ([Guid]::NewGuid().ToString('N') + '.out')
    $stderr = $stdout + '.err'
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer, '-ProfilePath', $Profile, '-CachePath', $Cache, '-Offline')
    if ($UseMirror) { $arguments += @('-MirrorPath', $mirror) }
    # Direct native invocation lets PowerShell 7 supply the Windows PowerShell module path. Under Windows PowerShell
    # a redirected native stderr line would otherwise become a terminating error here.
    $ErrorActionPreference = 'Continue'
    & powershell.exe @arguments 1> $stdout 2> $stderr
    $ErrorActionPreference = 'Stop'
    $text = (Get-Content $stderr -Raw) + (Get-Content $stdout -Raw)
    if (($LASTEXITCODE -eq 0) -ne $ShouldPass) { throw "Installer exit $LASTEXITCODE, expected pass=$ShouldPass`n$text" }
    $text
}
function Marker([string]$Profile) { Get-Content -LiteralPath (Join-Path $Profile '.earth-pack.json') -Raw | ConvertFrom-Json }

# 1. an existing unmarked folder (a normal VS profile) is never adopted
$protected = Join-Path $run 'existing-profile'
New-Item -ItemType Directory -Path $protected | Out-Null
Set-Content -LiteralPath (Join-Path $protected 'save-sentinel.txt') -Value 'keep'
Run-Installer $protected (Join-Path $run 'cache0') $false | Out-Null
Check ((Get-ChildItem -LiteralPath $protected).Count -eq 1) 'Existing folder was modified'

# 2. a corrupt cached download without network: fails, discards the corrupt file, installs nothing for it
$cache = Join-Path $run 'cache'
$profile = Join-Path $run 'profile'
New-Item -ItemType Directory -Path $cache | Out-Null
Set-Content -LiteralPath "$cache\modone-1.0.0.zip" -Value 'corrupt download'
$out = Run-Installer $profile $cache $false
Check ($out -match 'Discarding the damaged cached download') 'Corrupt cache reported'
Check (!(Test-Path "$cache\modone-1.0.0.zip")) 'Corrupt cached download deleted so the next run downloads it again'
Check (!(Marker $profile).complete) 'Failed installation marked ready'
Check (!(Test-Path "$profile\Mods\modone-1.0.0.zip")) 'Corrupt archive installed'

# 3. with the (simulated) network the next run downloads again and completes
Set-Content -LiteralPath "$cache\modtwo-2.0.0.zip" -Value 'another corrupt download'
Set-Content -LiteralPath "$cache\stale.download" -Value 'interrupted download'
Run-Installer $profile $cache $true -UseMirror | Out-Null
Check ((Sha "$cache\modtwo-2.0.0.zip") -eq (Sha "$mirror\modtwo-2.0.0.zip")) 'Corrupt cached download replaced by a verified one'
Check (!(Test-Path "$cache\stale.download")) 'Interrupted download leftovers removed'
Check (@(Get-ChildItem -LiteralPath "$profile\Mods" -Filter '*.zip').Count -eq 4) 'Complete mod set (2 bundled + 2 downloaded)'
Check ((Marker $profile).complete) 'Completed installation marked ready'
foreach ($z in Get-ChildItem -LiteralPath "$profile\Mods" -Filter '*.zip') {
    $source = if (Test-Path "$packDir\$($z.Name)") { "$packDir\$($z.Name)" } else { "$mirror\$($z.Name)" }
    Check ((Sha $z.FullName) -eq (Sha $source)) "Installed $($z.Name) is byte-identical"
}

# 4. an interrupted copy of an earlier installer (truncated archive under its final name) is repaired, offline
$truncated = "$profile\Mods\cylinderworld-1.0.0.zip"
$bytes = [IO.File]::ReadAllBytes($truncated)
[IO.File]::WriteAllBytes($truncated, $bytes[0..([int]($bytes.Length / 2))])
Set-Content -LiteralPath "$profile\Mods\earthmap-9.9.9.zip.partial" -Value 'interrupted copy'
Set-Content -LiteralPath "$profile\ModConfig\earthmap.json" -Value '{"RegionalTrees":true,"SpawnPlace":"57,25"}'
$configHash = Sha "$profile\ModConfig\earthmap.json"
$out = Run-Installer $profile $cache $true
Check ($out -match 'Replacing cylinderworld-1.0.0.zip') 'Damaged profile archive reported'
Check ((Sha $truncated) -eq (Sha "$packDir\cylinderworld-1.0.0.zip")) 'Damaged profile archive replaced'
Check (!(Test-Path "$profile\Mods\earthmap-9.9.9.zip.partial")) 'Partial copies removed'
Check ((Sha "$profile\ModConfig\earthmap.json") -eq $configHash) 'Config preserved'

# 5. idempotent re-run
$before = Get-ChildItem -LiteralPath "$profile\Mods" | ForEach-Object { $_.Name + ':' + $_.LastWriteTimeUtc.Ticks }
Run-Installer $profile $cache $true | Out-Null
$after = Get-ChildItem -LiteralPath "$profile\Mods" | ForEach-Object { $_.Name + ':' + $_.LastWriteTimeUtc.Ticks }
Check ((Compare-Object $before $after) -eq $null) 'Re-run leaves verified archives untouched'

# 6. a damaged bundled archive fails clearly and marks nothing ready
$profile2 = Join-Path $run 'profile2'
Copy-Item -LiteralPath "$packDir\earthmap-9.9.9.zip" -Destination "$run\earthmap-good.zip"
Set-Content -LiteralPath "$packDir\earthmap-9.9.9.zip" -Value 'damaged in transit'
$out = Run-Installer $profile2 $cache $false
Check ($out -match 'damaged') 'Damaged bundled archive reported'
Check (!(Marker $profile2).complete) 'Damaged pack not marked ready'
Move-Item -LiteralPath "$run\earthmap-good.zip" -Destination "$packDir\earthmap-9.9.9.zip" -Force

# 7. an unfinalized lock (the checked-in placeholder) is refused before anything is created
$lock.bundled[0].sha256 = 'SET-BY-tools/build-regional-tree-pack.ps1'
[IO.File]::WriteAllText("$packDir\pack-lock.json", ($lock | ConvertTo-Json -Depth 5))
$profile3 = Join-Path $run 'profile3'
$out = Run-Installer $profile3 $cache $false
Check ($out -match 'not finalized') 'Placeholder lock refused'
Check (!(Test-Path $profile3)) 'Nothing created for a placeholder lock'
[IO.File]::WriteAllText("$packDir\pack-lock.json", $lockJson)

# 8. the shipped lock parses and pins the expected content
$shipped = Get-Content -LiteralPath (Join-Path $root 'modpack\pack-lock.json') -Raw | ConvertFrom-Json
Check ($shipped.format -eq 2 -and @($shipped.mods).Count -eq 10 -and (@($shipped.bundled).id -join ',') -eq 'earthmap,cylinderworld') 'Shipped lock: Earth and Cylinder World bundled, ten downloads'
Check (@($shipped.mods | Where-Object { $_.url -notmatch '^https://moddbcdn\.vintagestory\.at/' }).Count -eq 0) 'Shipped downloads come from the Mod DB CDN'

Write-Host "PASS: $checks installer checks (existing-profile guard, corrupt cache discarded and re-downloaded, atomic replacement of damaged/partial archives, idempotence, config preservation, bundled-archive and placeholder-lock refusal; Windows PowerShell 5.1)."
Write-Host "Evidence: $run"
