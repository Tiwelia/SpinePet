[CmdletBinding()]
param(
    [string]$PlayerLogPath = (
        Join-Path $env:USERPROFILE `
            'AppData\LocalLow\com.proximabeta\NIKKE\Player.log'
    ),
    [string]$CatalogPath = (
        Join-Path $PSScriptRoot 'internal_ids.json'
    ),
    [string]$ResourceDirectory = (
        Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'res'
    ),
    [string]$NauPath = (
        Join-Path (Split-Path $PSScriptRoot -Parent) `
            'NikkeAssetUnpacker\NikkeAssetUnpacker.exe'
    ),
    [string]$PythonCommand = 'python',
    [string]$BaseUri,
    [switch]$Force,
    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'

function Get-DatapackBaseUri {
    param(
        [string]$LogPath
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "Player log was not found: $LogPath"
    }

    $pattern =
        'CatalogGroup\(\)\s+BaseUri:\s+' +
        '(?<uri>https://[^,\s]+/pck/dp/[^,\s]+/?)'
    $latestUri = $null
    foreach ($match in Select-String -LiteralPath $LogPath -Pattern $pattern) {
        $latestUri = $match.Matches[0].Groups['uri'].Value
    }

    if ([string]::IsNullOrWhiteSpace($latestUri)) {
        throw "No dp CatalogGroup BaseUri was found in: $LogPath"
    }

    return $latestUri.TrimEnd('/') + '/'
}

function Get-CharacterResources {
    param(
        [string]$Root
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Character resource directory was not found: $Root"
    }

    $resources = foreach (
        $skeleton in Get-ChildItem -LiteralPath $Root -Recurse -File `
            -Filter '*.skel'
    ) {
        if ($skeleton.BaseName -match '^(?<id>c\d+_[^_]+)') {
            $resourceDirectory = $skeleton.Directory
            if (
                $resourceDirectory.Name -in @('standing', 'aim', 'cover')
            ) {
                $characterDirectory = $resourceDirectory.Parent.FullName
            }
            else {
                $characterDirectory = $resourceDirectory.FullName
            }

            [pscustomobject]@{
                ResourceId = $Matches['id'].ToLowerInvariant()
                CharacterDirectory = $characterDirectory
            }
        }
    }

    return @(
        $resources |
            Sort-Object -Property ResourceId, CharacterDirectory -Unique
    )
}

function Get-IconBundles {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Internal ID catalog was not found: $Path"
    }

    $records = Get-Content -LiteralPath $Path -Encoding utf8 -Raw |
        ConvertFrom-Json
    $bundlePattern =
        '\\(?<bundle>icons-char-mi\(hd\)_assets_mi_' +
        '(?<id>c\d+_[^_]+)_s_[a-f0-9]+\.bundle)$'
    $bundles = @{}

    foreach ($record in $records) {
        $internalId = [string]$record.internal_id
        if ($internalId -notmatch $bundlePattern) {
            continue
        }

        $resourceId = $Matches['id'].ToLowerInvariant()
        if ($bundles.ContainsKey($resourceId)) {
            throw "Multiple HD icon bundles were found for $resourceId."
        }

        $bundles[$resourceId] = $Matches['bundle']
    }

    return $bundles
}

function Remove-WorkingDirectory {
    param(
        [string]$Path,
        [string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $directorySeparator = [System.IO.Path]::DirectorySeparatorChar
    $alternateSeparator = [System.IO.Path]::AltDirectorySeparatorChar
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd(
        $directorySeparator,
        $alternateSeparator
    )
    $rootPrefix = $resolvedRoot + $directorySeparator
    if (-not $resolvedPath.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to remove a directory outside ${resolvedRoot}: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

$resolvedBaseUri = if ([string]::IsNullOrWhiteSpace($BaseUri)) {
    Get-DatapackBaseUri -LogPath $PlayerLogPath
}
else {
    $BaseUri.TrimEnd('/') + '/'
}

$characterResources = Get-CharacterResources -Root $ResourceDirectory
$iconBundles = Get-IconBundles -Path $CatalogPath
$downloadPlan = foreach ($resource in $characterResources) {
    $resourceId = $resource.ResourceId
    if (-not $iconBundles.ContainsKey($resourceId)) {
        Write-Warning "No HD icon bundle was found for $resourceId."
        continue
    }

    [pscustomobject]@{
        ResourceId = $resourceId
        BundleName = $iconBundles[$resourceId]
        Uri = $resolvedBaseUri + $iconBundles[$resourceId]
        OutputPath = Join-Path (
            Join-Path $resource.CharacterDirectory 'icons'
        ) "${resourceId}_icon.png"
    }
}

Write-Output "Datapack BaseUri: $resolvedBaseUri"
$downloadPlan |
    Select-Object ResourceId, BundleName, OutputPath |
    Format-Table -AutoSize

if ($ListOnly) {
    Write-Output "ListOnly: matched $($downloadPlan.Count) icon(s)."
    return
}

if (-not (Test-Path -LiteralPath $NauPath -PathType Leaf)) {
    throw "NikkeAssetUnpacker was not found: $NauPath"
}

$extractorPath = Join-Path $PSScriptRoot 'extract_icon.py'
if (-not (Test-Path -LiteralPath $extractorPath -PathType Leaf)) {
    throw "UnityPy extractor was not found: $extractorPath"
}

& $PythonCommand -c 'import UnityPy, PIL'
if ($LASTEXITCODE -ne 0) {
    throw (
        "Python dependencies are missing. Run: " +
        "$PythonCommand -m pip install -r " +
        "`"$PSScriptRoot\requirements.txt`""
    )
}

$workingDirectory = Join-Path $ResourceDirectory (
    'SpinePet-Icons-' + [Guid]::NewGuid().ToString('N')
)
New-Item -ItemType Directory -Path $workingDirectory | Out-Null

$downloaded = 0
$skipped = 0
$failures = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($item in $downloadPlan) {
        if ((Test-Path -LiteralPath $item.OutputPath) -and -not $Force) {
            Write-Output "SKIP $($item.ResourceId): icon already exists."
            $skipped++
            continue
        }

        try {
            $encryptedPath = Join-Path $workingDirectory $item.BundleName
            Write-Output "DOWNLOAD $($item.ResourceId): $($item.Uri)"
            Invoke-WebRequest -Uri $item.Uri -OutFile $encryptedPath `
                -UseBasicParsing

            & $NauPath $encryptedPath
            if ($LASTEXITCODE -ne 0) {
                throw "NAU exited with code $LASTEXITCODE."
            }

            $decryptedName =
                [System.IO.Path]::GetFileNameWithoutExtension(
                    $item.BundleName
                ) + '_dec.bundle'
            $decryptedPath = Join-Path $workingDirectory $decryptedName
            if (-not (Test-Path -LiteralPath $decryptedPath -PathType Leaf)) {
                throw "NAU did not produce a decrypted bundle."
            }

            $temporaryIconPath = Join-Path (
                $workingDirectory
            ) "$($item.ResourceId)_icon.png"
            & $PythonCommand $extractorPath `
                --bundle $decryptedPath `
                --resource-id $item.ResourceId `
                --output $temporaryIconPath
            if ($LASTEXITCODE -ne 0 -or
                -not (Test-Path -LiteralPath $temporaryIconPath -PathType Leaf)
            ) {
                throw "UnityPy icon extraction failed."
            }

            $iconDirectory = Split-Path -Parent $item.OutputPath
            New-Item -ItemType Directory -Path $iconDirectory -Force |
                Out-Null
            Move-Item -LiteralPath $temporaryIconPath `
                -Destination $item.OutputPath -Force
            Write-Output "SAVED $($item.ResourceId): $($item.OutputPath)"
            $downloaded++
        }
        catch {
            $message = "$($item.ResourceId): $($_.Exception.Message)"
            $failures.Add($message)
            Write-Warning $message
        }
    }
}
finally {
    Remove-WorkingDirectory `
        -Path $workingDirectory `
        -AllowedRoot $ResourceDirectory
}

Write-Output (
    "Done. downloaded=$downloaded skipped=$skipped failed=$($failures.Count)"
)
if ($failures.Count -gt 0) {
    throw "Icon update failed: $($failures -join '; ')"
}
