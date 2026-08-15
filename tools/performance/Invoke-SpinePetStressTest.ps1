[CmdletBinding()]
param(
    [int[]]$CharacterCounts = @(1, 2, 4, 8),
    [ValidateRange(4, 120)]
    [int]$SteadyStateSeconds = 12,
    [ValidateRange(15, 300)]
    [int]$StartupTimeoutSeconds = 120,
    [ValidateSet(30, 60, 120)]
    [int]$TargetFrameRate = 120,
    [ValidateRange(512, 32768)]
    [int]$MaximumWorkingSetMB = 8192,
    [switch]$SkipGpuCounters,
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\..\src\SpinePet\bin\Release\net9.0-windows\SpinePet.exe'),
    [string]$SourceConfigPath = (Join-Path $env:LOCALAPPDATA 'SpinePet\config.json'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\.artifacts\performance')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$SourceConfigPath = [IO.Path]::GetFullPath($SourceConfigPath)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$logicalProcessorCount = [Environment]::ProcessorCount
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
$performancePattern = [regex]::new(
    'performance target-fps=(?<target>\d+) actual-fps=(?<fps>[\d.]+) visible=(?<visible>\d+) avg-frame-ms=(?<average>[\d.]+) max-frame-ms=(?<maximum>[\d.]+) allocated-bytes-per-frame=(?<allocated>\d+)',
    [Text.RegularExpressions.RegexOptions]::Compiled)

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Release executable not found: $ExecutablePath"
}

if (-not (Test-Path -LiteralPath $SourceConfigPath)) {
    throw "Source configuration not found: $SourceConfigPath"
}

if (Get-Process -Name SpinePet -ErrorAction SilentlyContinue) {
    throw 'Close the running SpinePet instance before starting the stress test.'
}

$sourceConfig = Get-Content -LiteralPath $SourceConfigPath -Raw |
    ConvertFrom-Json
$sourceCharacters = @($sourceConfig.Characters)
if ($sourceCharacters.Count -eq 0) {
    throw 'The source configuration does not contain any characters.'
}

$runDirectory = Join-Path $OutputRoot (
    'stress-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()

function Copy-JsonObject {
    param([Parameter(Mandatory)]$InputObject)

    return $InputObject |
        ConvertTo-Json -Depth 20 -Compress |
        ConvertFrom-Json
}

function New-ScenarioConfiguration {
    param(
        [Parameter(Mandatory)][int]$CharacterCount,
        [Parameter(Mandatory)][string]$ScenarioDirectory
    )

    $scenarioConfig = Copy-JsonObject $sourceConfig
    $scenarioConfig.Global.TargetFrameRate = $TargetFrameRate
    $configuredCharacters = [Collections.Generic.List[object]]::new()
    $entryCount = [Math]::Max($sourceCharacters.Count, $CharacterCount)
    for ($index = 0; $index -lt $entryCount; $index++) {
        $template = $sourceCharacters[$index % $sourceCharacters.Count]
        $character = Copy-JsonObject $template
        if ($index -ge $sourceCharacters.Count) {
            $character.Id = [Guid]::NewGuid().ToString('D')
            $character.Name = "{0} Stress {1}" -f $template.Name, ($index + 1)
        }

        $character.Visible = $index -lt $CharacterCount
        if ($character.Visible) {
            $column = $index % 4
            $row = [Math]::Floor($index / 4)
            $character.PositionX = 180 + ($column * 420)
            $character.PositionY = 900 - ($row * 110)
        }

        $configuredCharacters.Add($character)
    }

    $scenarioConfig.Characters = $configuredCharacters.ToArray()
    New-Item -ItemType Directory -Path $ScenarioDirectory -Force |
        Out-Null
    $configPath = Join-Path $ScenarioDirectory 'config.json'
    $json = $scenarioConfig | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($configPath, $json)
}

function Get-PerformanceSamples {
    param([Parameter(Mandatory)][string]$LogPath)

    if (-not (Test-Path -LiteralPath $LogPath)) {
        return @()
    }

    $samples = [Collections.Generic.List[object]]::new()
    foreach ($line in Get-Content -LiteralPath $LogPath) {
        $match = $performancePattern.Match($line)
        if (-not $match.Success) {
            continue
        }

        $samples.Add([pscustomobject]@{
            TargetFPS = [int]$match.Groups['target'].Value
            ActualFPS = [double]::Parse(
                $match.Groups['fps'].Value,
                $invariantCulture)
            VisibleCharacters = [int]$match.Groups['visible'].Value
            AverageFrameMS = [double]::Parse(
                $match.Groups['average'].Value,
                $invariantCulture)
            MaximumFrameMS = [double]::Parse(
                $match.Groups['maximum'].Value,
                $invariantCulture)
            AllocatedBytesPerFrame =
                [long]$match.Groups['allocated'].Value
        })
    }

    return $samples.ToArray()
}

function Get-GpuSnapshot {
    param([Parameter(Mandatory)][int]$ProcessId)

    try {
        $instancePattern = "pid_$ProcessId(_|$)"
        $engineSamples = (Get-Counter -Counter '\GPU Engine(*)\Utilization Percentage' -ErrorAction Stop).CounterSamples |
            Where-Object InstanceName -Match $instancePattern
        $memorySamples = (Get-Counter -Counter '\GPU Process Memory(*)\Dedicated Usage' -ErrorAction Stop).CounterSamples |
            Where-Object InstanceName -Match $instancePattern
        $sharedSamples = (Get-Counter -Counter '\GPU Process Memory(*)\Shared Usage' -ErrorAction Stop).CounterSamples |
            Where-Object InstanceName -Match $instancePattern

        return [pscustomobject]@{
            Utilization = [double](
                $engineSamples |
                Measure-Object -Property CookedValue -Sum).Sum
            DedicatedBytes = [double](
                $memorySamples |
                Measure-Object -Property CookedValue -Sum).Sum
            SharedBytes = [double](
                $sharedSamples |
                Measure-Object -Property CookedValue -Sum).Sum
        }
    }
    catch {
        return $null
    }
}

foreach ($characterCount in $CharacterCounts) {
    if ($characterCount -lt 1) {
        continue
    }

    Write-Host "Starting $characterCount-character scenario..."
    $scenarioDirectory = Join-Path $runDirectory (
        "characters-$characterCount")
    $scenarioArguments = @{
        CharacterCount = $characterCount
        ScenarioDirectory = $scenarioDirectory
    }
    New-ScenarioConfiguration @scenarioArguments
    $logPath = Join-Path $scenarioDirectory (
        'Logs\spinepet-' + (Get-Date -Format 'yyyyMMdd') + '.log')
    $process = $null
    $startupStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $startupSeconds = $null
    $loadedCharacters = 0
    $status = 'Completed'

    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $ExecutablePath
        $startInfo.WorkingDirectory = Split-Path -Parent $ExecutablePath
        $startInfo.UseShellExecute = $false
        $startInfo.Environment['SPINEPET_DATA_DIRECTORY'] =
            $scenarioDirectory
        $startInfo.Environment['SPINEPET_PERF_LOG'] = '1'
        $process = [Diagnostics.Process]::Start($startInfo)

        $panelClosed = $false
        while ($startupStopwatch.Elapsed.TotalSeconds -lt
               $StartupTimeoutSeconds) {
            if ($process.HasExited) {
                $status = "ExitedDuringStartup($($process.ExitCode))"
                break
            }

            $process.Refresh()
            if (-not $panelClosed -and $process.MainWindowHandle -ne 0) {
                Start-Sleep -Milliseconds 500
                $null = $process.CloseMainWindow()
                $panelClosed = $true
            }

            $startupSamples = @(Get-PerformanceSamples -LogPath $logPath)
            if ($startupSamples.Count -gt 0) {
                $loadedCharacters = ($startupSamples |
                    Measure-Object -Property VisibleCharacters -Maximum).Maximum
                if ($loadedCharacters -ge $characterCount) {
                    $startupSeconds = $startupStopwatch.Elapsed.TotalSeconds
                    break
                }
            }

            if (($process.WorkingSet64 / 1MB) -gt
                $MaximumWorkingSetMB) {
                $status = 'StartupMemoryLimitExceeded'
                break
            }

            Start-Sleep -Milliseconds 500
        }

        if ($null -eq $startupSeconds -and $status -eq 'Completed') {
            $status = 'StartupTimeout'
        }

        if ($status -eq 'Completed') {
            Start-Sleep -Seconds 2
            $samplesBefore = @(Get-PerformanceSamples -LogPath $logPath).Count
            $process.Refresh()
            $cpuStart = $process.TotalProcessorTime.TotalSeconds
            $steadyStopwatch = [Diagnostics.Stopwatch]::StartNew()
            $workingSets = [Collections.Generic.List[double]]::new()
            $privateBytes = [Collections.Generic.List[double]]::new()
            $handleCounts = [Collections.Generic.List[int]]::new()
            $threadCounts = [Collections.Generic.List[int]]::new()
            $gpuUtilization = [Collections.Generic.List[double]]::new()
            $gpuDedicatedMB = [Collections.Generic.List[double]]::new()
            $gpuSharedMB = [Collections.Generic.List[double]]::new()

            while ($steadyStopwatch.Elapsed.TotalSeconds -lt
                   $SteadyStateSeconds) {
                if ($process.HasExited) {
                    $status = "ExitedDuringSample($($process.ExitCode))"
                    break
                }

                $process.Refresh()
                $workingSetMB = $process.WorkingSet64 / 1MB
                $workingSets.Add($workingSetMB)
                $privateBytes.Add($process.PrivateMemorySize64 / 1MB)
                $handleCounts.Add($process.HandleCount)
                $threadCounts.Add($process.Threads.Count)
                if (-not $SkipGpuCounters) {
                    $gpu = Get-GpuSnapshot -ProcessId $process.Id
                    if ($null -ne $gpu) {
                        $gpuUtilization.Add($gpu.Utilization)
                        $gpuDedicatedMB.Add($gpu.DedicatedBytes / 1MB)
                        $gpuSharedMB.Add($gpu.SharedBytes / 1MB)
                    }
                }

                if ($workingSetMB -gt $MaximumWorkingSetMB) {
                    $status = 'SampleMemoryLimitExceeded'
                    break
                }

                Start-Sleep -Seconds 1
            }

            $process.Refresh()
            $cpuSeconds =
                $process.TotalProcessorTime.TotalSeconds - $cpuStart
            $wallSeconds = $steadyStopwatch.Elapsed.TotalSeconds
            $allSamples = @(Get-PerformanceSamples -LogPath $logPath)
            $steadySamples = @($allSamples | Select-Object -Skip $samplesBefore)
            $matchingSamples = @($steadySamples | Where-Object {
                $_.VisibleCharacters -eq $characterCount
            })
            if ($matchingSamples.Count -eq 0) {
                $matchingSamples = $steadySamples
            }

            $results.Add([pscustomobject]@{
                Characters = $characterCount
                TargetFPS = $TargetFrameRate
                Status = $status
                StartupSeconds = if ($null -eq $startupSeconds) {
                    [math]::Round($startupStopwatch.Elapsed.TotalSeconds, 2)
                } else {
                    [math]::Round($startupSeconds, 2)
                }
                LoadedCharacters = $loadedCharacters
                SampleSeconds = [math]::Round($wallSeconds, 2)
                ActualFPSAverage = if ($matchingSamples.Count) {
                    [math]::Round(($matchingSamples |
                        Measure-Object ActualFPS -Average).Average, 2)
                } else { $null }
                ActualFPSMinimum = if ($matchingSamples.Count) {
                    [math]::Round(($matchingSamples |
                        Measure-Object ActualFPS -Minimum).Minimum, 2)
                } else { $null }
                AverageFrameMS = if ($matchingSamples.Count) {
                    [math]::Round(($matchingSamples |
                        Measure-Object AverageFrameMS -Average).Average, 3)
                } else { $null }
                MaximumFrameMS = if ($matchingSamples.Count) {
                    [math]::Round(($matchingSamples |
                        Measure-Object MaximumFrameMS -Maximum).Maximum, 3)
                } else { $null }
                AllocatedBytesPerFrame = if ($matchingSamples.Count) {
                    [math]::Round(($matchingSamples |
                        Measure-Object AllocatedBytesPerFrame -Average).Average)
                } else { $null }
                CpuOneCorePercent = [math]::Round(
                    $cpuSeconds / $wallSeconds * 100,
                    2)
                CpuMachinePercent = [math]::Round(
                    $cpuSeconds / $wallSeconds /
                    $logicalProcessorCount * 100,
                    2)
                WorkingSetAverageMB = [math]::Round(($workingSets |
                    Measure-Object -Average).Average, 1)
                WorkingSetPeakMB = [math]::Round(($workingSets |
                    Measure-Object -Maximum).Maximum, 1)
                PrivateMemoryPeakMB = [math]::Round(($privateBytes |
                    Measure-Object -Maximum).Maximum, 1)
                HandleCountPeak = ($handleCounts |
                    Measure-Object -Maximum).Maximum
                ThreadCountPeak = ($threadCounts |
                    Measure-Object -Maximum).Maximum
                GpuUtilizationAverage = if ($gpuUtilization.Count) {
                    [math]::Round(($gpuUtilization |
                        Measure-Object -Average).Average, 2)
                } else { $null }
                GpuUtilizationPeak = if ($gpuUtilization.Count) {
                    [math]::Round(($gpuUtilization |
                        Measure-Object -Maximum).Maximum, 2)
                } else { $null }
                GpuDedicatedPeakMB = if ($gpuDedicatedMB.Count) {
                    [math]::Round(($gpuDedicatedMB |
                        Measure-Object -Maximum).Maximum, 1)
                } else { $null }
                GpuSharedPeakMB = if ($gpuSharedMB.Count) {
                    [math]::Round(($gpuSharedMB |
                        Measure-Object -Maximum).Maximum, 1)
                } else { $null }
            })
        }
        else {
            $results.Add([pscustomobject]@{
                Characters = $characterCount
                TargetFPS = $TargetFrameRate
                Status = $status
                StartupSeconds = [math]::Round(
                    $startupStopwatch.Elapsed.TotalSeconds,
                    2)
                LoadedCharacters = $loadedCharacters
            })
        }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
            try {
                $null = $process.WaitForExit(5000)
            }
            catch {
                # The process is already gone or inaccessible.
            }
        }
    }

    $latestResult = $results[$results.Count - 1]
    Write-Host (
        "Finished {0} characters: {1}, FPS={2}, peak working set={3} MB" -f
        $characterCount,
        $latestResult.Status,
        $latestResult.ActualFPSAverage,
        $latestResult.WorkingSetPeakMB)
    if ($latestResult.Status -ne 'Completed' -or
        ($null -ne $latestResult.WorkingSetPeakMB -and
         $latestResult.WorkingSetPeakMB -gt $MaximumWorkingSetMB)) {
        Write-Warning 'Safety threshold reached; remaining scenarios were skipped.'
        break
    }
}

$jsonPath = Join-Path $runDirectory 'stress-results.json'
$csvPath = Join-Path $runDirectory 'stress-results.csv'
[IO.File]::WriteAllText(
    $jsonPath,
    ($results | ConvertTo-Json -Depth 10))
$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

$tableProperties = @(
    'Characters',
    'Status',
    'StartupSeconds',
    'ActualFPSAverage',
    'ActualFPSMinimum',
    'AverageFrameMS',
    'MaximumFrameMS',
    'CpuOneCorePercent',
    'WorkingSetPeakMB',
    'GpuUtilizationAverage',
    'AllocatedBytesPerFrame'
)
$results | Format-Table -Property $tableProperties -AutoSize
Write-Host "JSON report: $jsonPath"
Write-Host "CSV report:  $csvPath"
