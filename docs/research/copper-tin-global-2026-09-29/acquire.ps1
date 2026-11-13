$ErrorActionPreference = 'Stop'
$researchRoot = $PSScriptRoot
$targets = @{
 'copper-assessment-gis.zip' = 'https://pubs.usgs.gov/sir/2010/5090/z/sir20105090z_gis.zip'
 'copper-assessment-report.pdf' = 'https://pubs.usgs.gov/sir/2010/5090/z/sir20105090z.pdf'
 'mrds-csv.zip' = 'https://mrdata.usgs.gov/mrds/mrds-csv.zip'
 'mrds-metadata.xml' = 'https://mrdata.usgs.gov/metadata/mrds.xml'
 'mrds-about.html' = 'https://mrdata.usgs.gov/mrds/about.php'
 'sedcu-csv.zip' = 'https://mrdata.usgs.gov/sedcu/sedcu-csv.zip'
 'sedcu-metadata.xml' = 'https://mrdata.usgs.gov/metadata/ofr-2003-of03-107.xml'
}
$records = foreach ($entry in $targets.GetEnumerator()) {
 $record = [ordered]@{file="raw/$($entry.Key)"; url=$entry.Value; retrieved_utc=[DateTime]::UtcNow.ToString('o')}
 try {
  $targetPath = Join-Path (Join-Path $researchRoot 'raw') $entry.Key
  $response = Invoke-WebRequest -Uri $entry.Value -OutFile $targetPath -PassThru -UseBasicParsing -TimeoutSec 90
  $record.status='downloaded'
  $record.bytes=(Get-Item -LiteralPath $targetPath).Length
  $record.sha256=(Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
  $record.content_type=$response.Headers['Content-Type']
  $record.last_modified=$response.Headers['Last-Modified']
 } catch { $record.status='failed'; $record.error=$_.Exception.Message }
 [PSCustomObject]$record
 Write-Host ($record | ConvertTo-Json -Compress)
}
$records | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $researchRoot 'powershell-download-manifest.json') -Encoding utf8
