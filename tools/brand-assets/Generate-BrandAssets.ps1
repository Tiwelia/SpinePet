param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\..\src\SpinePet\Assets\Brand")
)

$ErrorActionPreference = "Stop"

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputPath) | Out-Null

$python = Get-Command py -ErrorAction SilentlyContinue
if ($null -eq $python)
{
    throw "Python launcher 'py' was not found. Python 3 with Pillow is required."
}

& $python.Source -3 (Join-Path $PSScriptRoot "generate_brand_assets.py") `
    --source $sourcePath `
    --output-directory $outputPath

if ($LASTEXITCODE -ne 0)
{
    throw "Brand asset generation failed with exit code $LASTEXITCODE."
}
