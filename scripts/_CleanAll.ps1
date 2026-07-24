# _CleanAll.ps1 — batch-process all character folders in a single session.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$cleanScript = Join-Path $scriptDir '_CleanAtlas.ps1'
$charDir = Join-Path (Split-Path -Parent $scriptDir) 'res'

$folders = Get-ChildItem $charDir -Directory | Where-Object {
    (Get-ChildItem $_.FullName -Filter '*.atlas') -and
    (Get-ChildItem $_.FullName -Filter '*.skel')  -and
    (Get-ChildItem $_.FullName -Filter '*.png')
}

$total = 0
foreach ($f in $folders) {
    Write-Host "--- $($f.Name) ---"
    & $cleanScript $f.FullName
    $total++
}

Write-Host ""
Write-Host "Done. Processed $total folder(s)."
