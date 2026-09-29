$vec = (for ($i = 0; $i -lt 1024; $i++) { '0.5' }) .join(',')
$body = '{"vector": [' + $vec + '], "with_payload": true}'
$r = Invoke-RestMethod -Uri 'http://localhost:6333/collections/ProtoDrop/points/search?limit=1' -Method Post -Body $body -ContentType 'application/json'
foreach ($p in $r.result.Result) {
  Write-Host ('POINT id=' + $p.id)
  $json = $p.payload | ConvertTo-Json -Depth 4
  if ($json.Length -gt 3000) { $json.Substring(0, 3000) } else { $json }
}
