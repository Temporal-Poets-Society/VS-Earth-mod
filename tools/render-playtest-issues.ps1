[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/out/playtest-issues'
$inv=[Globalization.CultureInfo]::InvariantCulture
function Number($s){[double]::Parse($s,$inv)}
function Escape($s){[System.Net.WebUtility]::HtmlEncode($s)}
$before=@{}; Import-Csv "$root/before/sites.csv" | ForEach-Object {$before[$_.site]=$_}
$after=@(Import-Csv "$root/after/sites.csv")
$out=New-Object Text.StringBuilder
[void]$out.Append(@'
<!doctype html><meta charset="utf-8"><title>Earth pre.8 to pre.9 offline evidence</title>
<style>body{font:15px/1.5 system-ui;max-width:1200px;margin:24px auto;color:#23313d}table{border-collapse:collapse;margin:15px 0}td,th{padding:5px 12px;border:1px solid #ccc;text-align:right}th:first-child,td:first-child{text-align:left}.maps{display:flex;gap:24px}.maps img{width:338px;image-rendering:pixelated}figure{margin:12px 0}svg{background:#fafafa;border:1px solid #ddd}small{display:block;max-width:900px}section{border-top:1px solid #ddd;padding:15px 0}</style>
<h1>Earth pre.8 to pre.9: offline evidence</h1>
<p>512-high, sea 110, log1000, ring 409600, world width 512000, seed 386217, normal detail (pre.8 defaults). The user's seed was not supplied.</p>
<p><b>Surface-policy predictions, not screenshots of generated game chunks.</b> Each north-up map is 225 by 225 columns. Green = retained soil; brown = scree; grey = rock; white = snow/glacier; blue = water. These colours do not measure actual trees or grass. Rivers, caves and the full vanilla vegetation pipeline are not simulated here. Island/coast overlays are enabled. Percentages use eligible dry columns only.</p>
<p>Before terrain uses summit rules 1 and the pre.8 surface policy. Before tree picks compile the unmodified core at commit 28817e2. After uses production core rules. Game build and dedicated-server checks were blocked by denied access to the installed game assemblies; no client was launched.</p>
<table><tr><th>Summit</th><th>Reference m</th><th>Ideal Y</th><th>Before Y</th><th>After Y</th></tr>
'@)
foreach($a in $after){if((Number $a.referenceM) -gt 0){$b=$before[$a.site];[void]$out.Append(('<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td></tr>' -f (Escape $a.site),$a.referenceM,$a.idealY,$b.pointY,$a.pointY))}}
[void]$out.Append('</table><small>Point Y is the nearest column to the surveyed summit. The broad-area maximum in sites.csv can include neighbouring mountains (especially Everest/Lhotse). Corrections are confined to 1.4 km around Everest/Lhotse and 3 km around the other summits.</small>')
foreach($a in $after){
    $b=$before[$a.site]
    [void]$out.Append(('<section><h2>{0}</h2><p>{1}, {2}; warmest month {3} to {4} C; forest potential {5} to {6}; shrub potential {7} to {8}</p><div class="maps">' -f (Escape $a.site),$a.lat,$a.lon,$b.warmAtPoint,$a.warmAtPoint,$b.forest,$a.forest,$b.shrub,$a.shrub))
    foreach($stage in @('before','after')){
        $r=if($stage -eq 'before'){$b}else{$a}
        $file=Join-Path "$root/$stage" ($a.site.Replace(' ','-')+'.png')
        $encoded=[Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
        [void]$out.Append(('<figure><figcaption>{0}</figcaption><img alt="{1} {0} predicted cover" src="data:image/png;base64,{2}"><small>rock {3}%; scree {4}%; soil {5}%; ice/snow {6}%; {7} dry columns</small></figure>' -f $stage,(Escape $a.site),$encoded,$r.rockPct,$r.screePct,$r.soilPct,$r.icePct,$r.landColumns))
    }
    [void]$out.Append('</div></section>')
}
[void]$out.Append('<h2>Summit profiles</h2><p>Orange: old source and detail. Blue: corrected terrain. Each profile is 12 km long through the summit anchor; elevation is in metres.</p>')
$profiles=@(Import-Csv "$root/after/summit-profiles.csv")
foreach($peak in ($profiles.peak | Select-Object -Unique)){
    [void]$out.Append(('<h3>{0}</h3>' -f (Escape $peak)))
    foreach($axis in @('east','north')){
        $group=@($profiles | Where-Object {$_.peak -eq $peak -and $_.axis -eq $axis})
        $heights=@($group | ForEach-Object {(Number $_.beforeM);(Number $_.afterM)})
        $low=($heights | Measure-Object -Minimum).Minimum; $high=($heights | Measure-Object -Maximum).Maximum
        [void]$out.Append(('<figure><figcaption>{0}: {1:F0} to {2:F0} m</figcaption><svg width="560" height="180" role="img" aria-label="{3} {0} elevation profile">' -f $axis,$low,$high,(Escape $peak)))
        foreach($key in @('beforeM','afterM')){
            $color=if($key -eq 'beforeM'){'#be6e21'}else{'#237cc2'}
            $points=@($group | ForEach-Object {
                $x=10+((Number $_.distanceM)+6000)/12000*540; $y=170-((Number $_.$key)-$low)/[Math]::Max(1,$high-$low)*160
                $x.ToString('F2',$inv)+','+$y.ToString('F2',$inv)
            }) -join ' '
            [void]$out.Append(('<polyline fill="none" stroke="{0}" stroke-width="2" points="{1}"/>' -f $color,$points))
        }
        [void]$out.Append('</svg></figure>')
    }
}
[void]$out.Append('<h2>Hoggar vanilla species</h2><p>Expected shares per attempt before the additional desert planting cap: 2% on dry uplands, 15% near wadis. Original forest-map density still applies.</p><table><tr><th>Stage</th><th>Altitude</th><th>Wet</th><th>Generator</th><th>Share</th></tr>')
foreach($stage in @('before','after')){
    Import-Csv "$root/$stage/tree-picks.csv" | Where-Object {$_.providers -eq 'vanilla'} | ForEach-Object {
        [void]$out.Append(('<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4:F2}%</td></tr>' -f $stage,$_.altitude,$_.wet,(Escape $_.generator),((Number $_.expectedShare)*100)))
    }
}
[void]$out.Append('</table>')
[IO.File]::WriteAllText((Join-Path $root 'report.html'),$out.ToString(),(New-Object Text.UTF8Encoding($false)))
Write-Output (Join-Path $root 'report.html')
