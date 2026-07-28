[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$ResourceDirectory,
    [string]$CharacterNamesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ResourceDirectory)) {
    $repositoryRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..\..')
    )
    $ResourceDirectory = Join-Path $repositoryRoot 'res'
}

if ([string]::IsNullOrWhiteSpace($CharacterNamesPath)) {
    $CharacterNamesPath = Join-Path (
        Split-Path $ResourceDirectory -Parent
    ) 'docs\CharacterNames.json'
}

$ResourceDirectory = [System.IO.Path]::GetFullPath($ResourceDirectory)
$CharacterNamesPath = [System.IO.Path]::GetFullPath($CharacterNamesPath)
if (-not (Test-Path -LiteralPath $ResourceDirectory -PathType Container)) {
    throw "Resource directory does not exist: $ResourceDirectory"
}

if (-not (Test-Path -LiteralPath $CharacterNamesPath -PathType Leaf)) {
    throw "Character name map does not exist: $CharacterNamesPath"
}

$characterNames = Get-Content `
    -LiteralPath $CharacterNamesPath `
    -Encoding UTF8 `
    -Raw |
    ConvertFrom-Json
$invalidFileNameCharacters = [System.IO.Path]::GetInvalidFileNameChars()
$plans = [System.Collections.Generic.List[object]]::new()
$characterDirectories = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
$legacyDirectories = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)

foreach (
    $legacyDirectory in Get-ChildItem `
        -LiteralPath $ResourceDirectory `
        -Directory
) {
    $directSkeletons = @(
        Get-ChildItem `
            -LiteralPath $legacyDirectory.FullName `
            -File `
            -Filter '*.skel'
    )
    if ($directSkeletons.Count -eq 0) {
        continue
    }

    [void]$legacyDirectories.Add($legacyDirectory.FullName)
    foreach (
        $sourceFile in Get-ChildItem `
            -LiteralPath $legacyDirectory.FullName `
            -File
    ) {
        if ($sourceFile.Name -notmatch '^(?<resource>c(?<code>\d+)_(?<skin>[^_.]+))') {
            Write-Warning "Unrecognized file left in place: $($sourceFile.FullName)"
            continue
        }

        $characterCode = $Matches['code']
        $skinCode = $Matches['skin']
        $nameProperty =
            $characterNames.PSObject.Properties[$characterCode]
        $displayName = if ($null -ne $nameProperty) {
            [string]$nameProperty.Value
        }
        else {
            $legacyDirectory.Name -replace "_$([regex]::Escape($skinCode))$", ''
        }

        foreach ($invalidCharacter in $invalidFileNameCharacters) {
            $displayName = $displayName.Replace($invalidCharacter, '_')
        }

        $characterDirectory = Join-Path $ResourceDirectory $displayName.Trim()
        $resourceType = if (
            $sourceFile.BaseName.EndsWith(
                '_icon',
                [System.StringComparison]::OrdinalIgnoreCase
            )
        ) {
            'icons'
        }
        else {
            'standing'
        }
        $destinationDirectory =
            Join-Path $characterDirectory $resourceType
        $destinationPath =
            Join-Path $destinationDirectory $sourceFile.Name

        [void]$characterDirectories.Add($characterDirectory)
        $plans.Add([pscustomobject]@{
            Source = $sourceFile.FullName
            Destination = $destinationPath
            CharacterDirectory = $characterDirectory
            ResourceType = $resourceType
        })
    }
}

$duplicateDestinations = @(
    $plans |
        Group-Object -Property Destination |
        Where-Object Count -gt 1
)
if ($duplicateDestinations.Count -gt 0) {
    throw (
        'Multiple files map to the same destination: ' +
        ($duplicateDestinations.Name -join ', ')
    )
}

$conflicts = @(
    $plans |
        Where-Object {
            Test-Path -LiteralPath $_.Destination
        }
)
if ($conflicts.Count -gt 0) {
    throw (
        'Migration would overwrite existing files: ' +
        (($conflicts | ForEach-Object Destination) -join ', ')
    )
}

if ($plans.Count -eq 0) {
    Write-Host 'No legacy character folders require migration.'
    return
}

Write-Host "Planned file moves: $($plans.Count)"
foreach ($plan in $plans) {
    Write-Host "  $($plan.Source) -> $($plan.Destination)"
}

foreach ($characterDirectory in $characterDirectories) {
    foreach ($resourceType in @('standing', 'aim', 'cover', 'icons')) {
        $typeDirectory = Join-Path $characterDirectory $resourceType
        if ($PSCmdlet.ShouldProcess(
            $typeDirectory,
            'Create character resource directory'
        )) {
            New-Item `
                -ItemType Directory `
                -Path $typeDirectory `
                -Force |
                Out-Null
        }
    }
}

foreach ($plan in $plans) {
    if ($PSCmdlet.ShouldProcess(
        $plan.Source,
        "Move to $($plan.Destination)"
    )) {
        Move-Item `
            -LiteralPath $plan.Source `
            -Destination $plan.Destination
    }
}

foreach ($legacyDirectory in $legacyDirectories) {
    if (
        (Test-Path -LiteralPath $legacyDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $legacyDirectory -Force).Count -eq 0 -and
        $PSCmdlet.ShouldProcess(
            $legacyDirectory,
            'Remove empty legacy character directory'
        )
    ) {
        Remove-Item -LiteralPath $legacyDirectory
    }
}

Write-Host "Done. Migrated $($plans.Count) file(s)."
