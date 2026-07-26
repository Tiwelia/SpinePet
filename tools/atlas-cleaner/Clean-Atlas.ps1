# Removes atlas regions whose names do not appear in the matching skeleton.
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$Folder,

    [bool]$CreateBackup = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
    throw "Character directory does not exist: $Folder"
}

Push-Location -LiteralPath $Folder
try {
    $atlasFile = Get-ChildItem -Filter '*.atlas' | Select-Object -First 1
    $skeletonFile = Get-ChildItem -Filter '*.skel' | Select-Object -First 1
    $textureFile = Get-ChildItem -Filter '*.png' | Select-Object -First 1

    if (-not $atlasFile -or -not $skeletonFile -or -not $textureFile) {
        Write-Host '  SKIP: missing files'
        return
    }

    $skeletonBytes = [System.IO.File]::ReadAllBytes($skeletonFile.FullName)
    $skeletonText = [System.Text.Encoding]::UTF8.GetString($skeletonBytes)

    $lines = [System.IO.File]::ReadAllLines($atlasFile.FullName)
    $newLines = [System.Collections.Generic.List[string]]::new()
    $removed = 0
    $index = 0

    function Test-IsAtlasEntryHeader {
        param(
            [Parameter(Mandatory)]
            [string]$Line
        )

        if (
            [string]::IsNullOrWhiteSpace($Line) -or
            [char]::IsWhiteSpace($Line[0])
        ) {
            return $false
        }

        $candidate = $Line.Trim()
        return $candidate -notmatch ':'
    }

    function Test-IsRegionHeader {
        param(
            [Parameter(Mandatory)]
            [string]$Line
        )

        return (
            (Test-IsAtlasEntryHeader -Line $Line) -and
            $Line.Trim() -notmatch '\.(png|jpe?g|webp)$'
        )
    }

    while ($index -lt $lines.Count) {
        $line = $lines[$index]
        $trimmed = $line.Trim()

        if (
            (Test-IsRegionHeader -Line $line) -and
            $skeletonText.IndexOf(
                $trimmed,
                [System.StringComparison]::Ordinal
            ) -lt 0
        ) {
            Write-Host "  DEL: $trimmed"
            $removed++
            $index++

            while (
                $index -lt $lines.Count -and
                -not (Test-IsAtlasEntryHeader -Line $lines[$index])
            ) {
                $index++
            }

            continue
        }

        [void]$newLines.Add($line)
        $index++
    }

    if ($removed -gt 0) {
        if ($PSCmdlet.ShouldProcess(
            $atlasFile.FullName,
            "Remove $removed unused atlas region(s)"
        )) {
            if ($CreateBackup) {
                $backupPath = "$($atlasFile.FullName).bak"
                if (-not (Test-Path -LiteralPath $backupPath)) {
                    Copy-Item `
                        -LiteralPath $atlasFile.FullName `
                        -Destination $backupPath
                }
            }

            [System.IO.File]::WriteAllLines($atlasFile.FullName, $newLines)
        }
    }

    Write-Host "  removed=$removed"
}
finally {
    Pop-Location
}
