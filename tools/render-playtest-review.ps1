param()
$ErrorActionPreference='Stop'
$root=Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/out/playtest-issues'
$dir=Join-Path $root 'review'
function Escape($s){[Net.WebUtility]::HtmlEncode([string]$s)}
$html=[Text.StringBuilder]::new()
[void]$html.Append('<!doctype html><meta charset="utf-8"><title>Earth pre.9 review evidence</title><style>body{font:15px/1.5 system-ui;margin:24px;color:#223;max-width:1450px}table{border-collapse:collapse;font-size:13px}td,th{border:1px solid #bbb;padding:4px 8px}img{max-width:46%;image-rendering:pixelated;vertical-align:top;margin:10px}section{margin-top:28px}code{background:#eee}</style><h1>Earth pre.9 coordinator review</h1><p>Offline source/model evidence at 512-high, log1000, sea 110, world 512000 / ring 409600, seed 386217. These are terrain/policy renders, not game-client renders. Game assembly binding and actual ships remain for coordinator validation.</p><p><a href="report.html">Original alpine before/after maps and profiles</a> · <a href="review.log">Review test log</a></p>')
[void]$html.Append('<p><b>Production peak catalogue: </b>'+ (Escape ([IO.File]::ReadAllText((Join-Path $dir 'production-catalogue.txt'))))+'</p>')
foreach($name in @('summit-catalogue-handoff','global-desert-trees','coral-habitat','perennial-and-wadi-columns','reservoir-before-after','iraq-water-components','strait-clearance')){
    $rows=@(Import-Csv (Join-Path $dir ($name+'.csv')))
    [void]$html.Append('<section><h2>'+(Escape $name)+'</h2><p><a href="review/'+$name+'.csv">CSV evidence</a></p><table><tr>')
    foreach($col in $rows[0].PSObject.Properties.Name){[void]$html.Append('<th>'+(Escape $col)+'</th>')}
    [void]$html.Append('</tr>')
    foreach($row in $rows){[void]$html.Append('<tr>');foreach($v in $row.PSObject.Properties.Value){[void]$html.Append('<td>'+(Escape $v)+'</td>')};[void]$html.Append('</tr>')}
    [void]$html.Append('</table></section>')
}
[void]$html.Append('<h2>Straits: before / after</h2><p>Dark blue: six or more fluid blocks; light blue: shallower sea; brown: land. North up.</p>')
foreach($name in @('Bosporus','Dardanelles','Kerch','Messina','Menai')){
    [void]$html.Append('<section><h3>'+ $name+'</h3>')
    foreach($stage in @('before','after')){$base64=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $dir ($name+'-'+$stage+'.png'))));[void]$html.Append('<img alt="'+$name+' '+$stage+'" src="data:image/png;base64,'+$base64+'">')}
    [void]$html.Append('</section>')
}
[void]$html.Append('<p>The summit fixture uses supplied reference heights to test shaping. It cannot establish the accuracy or completeness of the pending production catalogue. Unnamed Iraqi marsh extents remain historically unresolved; source IDs and reservoir classifications are recorded in decision 0034.</p>')
[IO.File]::WriteAllText((Join-Path $root 'review-report.html'),$html.ToString(),[Text.UTF8Encoding]::new($false))
Write-Output (Join-Path $root 'review-report.html')
