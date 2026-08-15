# Batch-process all standing resource folders in a single session.
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ResourceDirectory,

    [bool]$CreateBackup = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
    throw 'Unable to determine the atlas-cleaner script directory.'
}

if ([string]::IsNullOrWhiteSpace($ResourceDirectory)) {
    $repositoryRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $scriptDirectory '..\..')
    )
    $ResourceDirectory = Join-Path $repositoryRoot 'res'
}

$ResourceDirectory = [System.IO.Path]::GetFullPath($ResourceDirectory)
$cleanScript = Join-Path $scriptDirectory 'Clean-Atlas.ps1'
if (-not (Test-Path -LiteralPath $ResourceDirectory -PathType Container)) {
    throw "Resource directory does not exist: $ResourceDirectory"
}

$folders = Get-ChildItem `
    -LiteralPath $ResourceDirectory `
    -Directory `
    -Recurse |
    Where-Object {
        [string]::Equals(
            $_.Name,
            'standing',
            [System.StringComparison]::OrdinalIgnoreCase
        ) -and
        (Get-ChildItem -LiteralPath $_.FullName -Filter '*.atlas') -and
        (Get-ChildItem -LiteralPath $_.FullName -Filter '*.skel') -and
        (Get-ChildItem -LiteralPath $_.FullName -Filter '*.png')
    } |
    Sort-Object FullName

$total = 0
foreach ($folder in $folders) {
    $relativePath = $folder.FullName.Substring(
        $ResourceDirectory.TrimEnd('\').Length
    ).TrimStart('\')
    Write-Host "--- $relativePath ---"
    & $cleanScript `
        -Folder $folder.FullName `
        -CreateBackup $CreateBackup `
        -WhatIf:$WhatIfPreference
    $total++
}

Write-Host ''
Write-Host "Done. Processed $total folder(s)."
