# _CleanAtlas.ps1 — removes watermark regions from Spine atlas files.
# A region is removed only when its name does NOT appear in the
# corresponding .skel binary. Uses a fast greedy regex (no lookaround)
# that correctly catches short names like "12".
param([string]$Folder)

$ErrorActionPreference = "Stop"
Push-Location $Folder

$atlasFile = Get-ChildItem -Filter "*.atlas" | Select-Object -First 1
$skelFile  = Get-ChildItem -Filter "*.skel"  | Select-Object -First 1
$pngFile   = Get-ChildItem -Filter "*.png"   | Select-Object -First 1

if (-not $atlasFile -or -not $skelFile -or -not $pngFile) {
    Write-Host "  SKIP: missing files"
    Pop-Location; exit 0
}

# - 1. Extract all identifier-like strings from .skel binary -
# Greedy regex without lookaround — fast and catches 2-char names.
$skelBytes  = [System.IO.File]::ReadAllBytes($skelFile.FullName)
$skelText   = [System.Text.Encoding]::ASCII.GetString($skelBytes)
$validNames = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($m in [regex]::Matches($skelText, '[a-zA-Z0-9][a-zA-Z0-9_]+')) {
    [void]$validNames.Add($m.Value)
}

# - 2. Scan atlas and remove unmatched regions -
$headerWords = [string[]]@(
    'size','filter','pma','true','false','repeat','format','index'
)

$lines    = [System.IO.File]::ReadAllLines($atlasFile.FullName)
$newLines = New-Object 'System.Collections.Generic.List[string]'
$removed  = 0
$i = 0

while ($i -lt $lines.Count) {
    $line    = $lines[$i]
    $trimmed = $line.Trim()

    $isRegionName = ($trimmed -match '^[a-zA-Z0-9][a-zA-Z0-9_]*$') -and
                    ($trimmed -notin $headerWords)

    if ($isRegionName) {
        $nextIdx = $i + 1
        if ($nextIdx -lt $lines.Count) {
            $nextTrimmed = $lines[$nextIdx].Trim()
            if ($nextTrimmed -match '^(bounds|rotate|offsets|index|size):') {
                if (-not $validNames.Contains($trimmed)) {
                    Write-Host "  DEL: $trimmed"
                    $removed++
                    $i += 2
                    continue
                }
            }
        }
    }

    [void]$newLines.Add($line)
    $i++
}

if ($removed -gt 0) {
    [System.IO.File]::WriteAllLines($atlasFile.FullName, $newLines)
}

Write-Host "  removed=$removed"
Pop-Location
