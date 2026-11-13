# Links the untracked inputs of the ore bakes (git-ignored raw downloads, normalized tables and preview grids) from the
# main checkout into a worktree, so tools/bake_ore_heatmaps.py runs there: folders as directory junctions, single files
# copied. Read the linked inputs only; new downloads go to C:\VS mods\Earth\rawdata\ore\<group>\ (decision 0065).
param([string]$Worktree = (Resolve-Path "$PSScriptRoot\..\..").Path, [string]$Main = 'C:\VS mods\Earth')
$ErrorActionPreference = 'Stop'
if ((Resolve-Path $Worktree).Path -eq (Resolve-Path $Main).Path) { 'main checkout: nothing to link'; return }
$roots = 'docs/research/remaining-ores-2026-09-29', 'docs/research/copper-tin-global-2026-09-29', 'preview'
$ignored = git -C $Main status --ignored --porcelain -- @roots | Where-Object { $_ -like '!! *' } | ForEach-Object { $_.Substring(3).Trim('"') } |
    Where-Object { $_ -notmatch '__pycache__' }
if ($ignored -notcontains 'preview/') { $ignored += 'preview/' }
foreach ($rel in $ignored) {
    $isDir = $rel.EndsWith('/'); $rel = $rel.TrimEnd('/') -replace '/', '\'
    $target = Join-Path $Main $rel; $link = Join-Path $Worktree $rel
    if ($isDir -and (Test-Path $link) -and !((Get-Item $link).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        # a folder with tracked files: hard-link the missing ignored files one by one
        $n = 0
        Get-ChildItem -Recurse -File $target | ForEach-Object {
            $dest = Join-Path $link $_.FullName.Substring($target.Length + 1)
            if (!(Test-Path $dest)) { New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null; New-Item -ItemType HardLink -Path $dest -Target $_.FullName | Out-Null; $n++ }
        }
        "hard-linked $n files into: $rel"; continue
    }
    if (Test-Path $link) { "exists: $rel"; continue }
    if (!(Test-Path $target)) { "missing in main checkout: $rel"; continue }
    New-Item -ItemType Directory -Force (Split-Path $link) | Out-Null
    if ($isDir) { New-Item -ItemType Junction -Path $link -Target $target | Out-Null; "linked: $rel" }
    else { Copy-Item $target $link; "copied: $rel" }
}
