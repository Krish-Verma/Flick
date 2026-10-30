<#
.SYNOPSIS
Matched synthetic WPF diagnostics on one fresh Windows GitHub Actions runner.
.DESCRIPTION
Builds the same harness source against pinned baseline and candidate checkouts.
Three repetitions of 0/50/1000/5000 offline rows with 64 bounded local PNG
stills, with balanced overall AB/BA ordering. Each observation runs in a fresh process and marker-owned profile.
Default 15-second samples are short CI diagnostics. Set SampleSeconds 300 for
five-minute synthetic samples; neither protocol measures gameplay or normal
app idle, capture, video playback, audio, Narrator, external drag or per-monitor DPI.
#>
param(
    [Parameter(Mandatory=$true)][string]$BaselineCheckout,
    [Parameter(Mandatory=$true)][string]$CandidateCheckout,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,300)][int]$SampleSeconds = 15
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows -or $env:GITHUB_ACTIONS -cne 'true') {
    throw 'Use a fresh Windows GitHub Actions runner. No existing Flick profile will be replaced.'
}
$baselineRevision = '49844256bf5f160b4777d0fa057cd79ddcf27eaf'
$BaselineCheckout = (Resolve-Path -LiteralPath $BaselineCheckout).Path
$CandidateCheckout = (Resolve-Path -LiteralPath $CandidateCheckout).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($BaselineCheckout -eq $CandidateCheckout) { throw 'Baseline and candidate need separate checkouts.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh output directory; earlier evidence is preserved.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$actualBaseline = (& git -C $BaselineCheckout rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualBaseline -ne $baselineRevision) { throw 'Baseline checkout does not match the pinned main revision.' }
$candidateRevision = (& git -C $CandidateCheckout rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve candidate revision.' }
foreach ($checkout in @($BaselineCheckout,$CandidateCheckout)) {
    $dirty = @(& git -C $checkout status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw "Use a clean checkout for exact-revision evidence: $checkout" }
}
$harnessSource = Join-Path $CandidateCheckout 'tools/Flick.UiBenchmark'
$baselineHarness = Join-Path $BaselineCheckout 'tools/Flick.UiBenchmark'
if (Test-Path -LiteralPath $baselineHarness) { throw 'Use a fresh baseline checkout without an existing benchmark harness.' }
New-Item -ItemType Directory -Path $baselineHarness | Out-Null
$harnessFiles = @(Get-ChildItem -LiteralPath $harnessSource -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($harnessSource, $_.FullName)
    $destination = Join-Path $baselineHarness $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
    $digest = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    $copiedDigest = (Get-FileHash -LiteralPath (Join-Path $baselineHarness $relative) -Algorithm SHA256).Hash
    if ($digest -ne $copiedDigest) { throw "Harness source differs after copying: $relative" }
    [pscustomobject]@{ path=$relative; sha256=$digest }
})
$counts = @(0,50,1000,5000)
$sequence = @()
for ($repetition = 1; $repetition -le 3; $repetition++) {
    for ($dataset = 0; $dataset -lt $counts.Count; $dataset++) {
        # Three is odd: per-dataset first-position counts cannot match exactly.
        # Reversing adjacent datasets balances total AB/BA exposure 6:6.
        $order = if (($repetition + $dataset) % 2 -eq 0) { @('candidate','baseline') } else { @('baseline','candidate') }
        foreach ($variant in $order) {
            $sequence += [pscustomobject]@{
                ordinal=$sequence.Count+1; repetition=$repetition; rowCount=$counts[$dataset]; variant=$variant
                revision=$(if ($variant -eq 'baseline') { $baselineRevision } else { $candidateRevision })
                file=('rep{0}-rows{1}-{2}.json' -f $repetition,$counts[$dataset],$variant)
            }
        }
    }
}
$manifest = [pscustomobject]@{
    schemaVersion=1; baselineRevision=$baselineRevision; candidateRevision=$candidateRevision
    sampleSeconds=$SampleSeconds; repetitions=3; counts=$counts; sequence=$sequence; harnessFiles=$harnessFiles
    evidenceClass=$(if ($SampleSeconds -lt 300) { 'synthetic-short-CI-diagnostic' } else { 'synthetic-five-minute-protocol' })
    runner=$env:RUNNER_NAME; startedUtc=[DateTimeOffset]::UtcNow.ToString('o')
    order='12 matched pairs; 6 baseline-first and 6 candidate-first overall; odd per-dataset repetitions leave 2:1 first-position exposure'
    quantiles='Nearest-rank empirical quantiles, pooled raw observations within each dataset; no confidence interval or independence claim'
    notes='Same runner and harness sources; fresh processes and marker-owned profiles; real synthetic PNG still decoding; no normal-app polling, capture, gameplay, video playback, audio, global hotkeys, registry operations or performance certification. Startup/font/OS cache effects and runner noise remain possible. Memory delta over 10 MiB is an investigation flag, not an automatic performance failure.'
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding utf8
$buildRoot = Join-Path $env:RUNNER_TEMP ('Flick-UiBenchmark-build-' + [Guid]::NewGuid().ToString('N'))
$checkouts = @{ baseline=$BaselineCheckout; candidate=$CandidateCheckout }
$executables = @{}

function Get-Quantile([double[]]$Values, [double]$Probability) {
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Min($sorted.Count-1, [Math]::Ceiling($sorted.Count * $Probability)-1))
    return [double]$sorted[$index]
}

function Write-Comparison([string]$Status, [string]$Failure = '') {
    $runs = @()
    foreach ($entry in $sequence) {
        $path = Join-Path $OutputDirectory $entry.file
        if (!(Test-Path -LiteralPath $path)) { continue }
        $data = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($data.status -eq 'passed') {
            $data | Add-Member -NotePropertyName repetition -NotePropertyValue $entry.repetition
            $data | Add-Member -NotePropertyName rawFile -NotePropertyValue $entry.file
            $runs += $data
        }
    }
    $comparisons = @()
    $investigations = @()
    foreach ($count in $counts) {
        $base = @($runs | Where-Object { $_.variant -eq 'baseline' -and $_.rowCount -eq $count })
        $candidate = @($runs | Where-Object { $_.variant -eq 'candidate' -and $_.rowCount -eq $count })
        if ($base.Count -ne 3 -or $candidate.Count -ne 3) { continue }
        $baseResources = @($base | ForEach-Object { $_.resources })
        $candidateResources = @($candidate | ForEach-Object { $_.resources })
        $metrics = @(
            @{ name='privateBytes'; unit='bytes'; baseline=@($baseResources.privateBytes); candidate=@($candidateResources.privateBytes) },
            @{ name='workingSetBytes'; unit='bytes'; baseline=@($baseResources.workingSetBytes); candidate=@($candidateResources.workingSetBytes) },
            @{ name='cpuOneCorePercent'; unit='percent-of-one-logical-core'; baseline=@($baseResources.cpuOneCorePercent); candidate=@($candidateResources.cpuOneCorePercent) },
            @{ name='queryKeystrokeToSettled'; unit='milliseconds-including-binding-debounce'; baseline=@($base | ForEach-Object { $_.queryKeystrokeToSettled.milliseconds }); candidate=@($candidate | ForEach-Object { $_.queryKeystrokeToSettled.milliseconds }) },
            @{ name='filterAndLayoutAfterDebounce'; unit='milliseconds'; baseline=@($base | ForEach-Object { $_.filterAndLayoutAfterDebounceMs }); candidate=@($candidate | ForEach-Object { $_.filterAndLayoutAfterDebounceMs }) },
            @{ name='scrollAndLayout'; unit='milliseconds-not-presented-frame-time'; baseline=@($base | ForEach-Object { $_.scrollAndLayoutMs }); candidate=@($candidate | ForEach-Object { $_.scrollAndLayoutMs }) },
            @{ name='scrollAndStillsReady'; unit='milliseconds-including-production-row-PNG-decoding'; baseline=@($base | ForEach-Object { $_.scrollAndStillsReadyMs }); candidate=@($candidate | ForEach-Object { $_.scrollAndStillsReadyMs }) },
            @{ name='layout'; unit='milliseconds'; baseline=@($base | ForEach-Object { $_.layoutMs }); candidate=@($candidate | ForEach-Object { $_.layoutMs }) },
            @{ name='settingsConstructionAndLayout'; unit='milliseconds-no-shell-navigation-or-OS-discovery'; baseline=@($base | ForEach-Object { $_.settingsConstructionAndLayoutMs }); candidate=@($candidate | ForEach-Object { $_.settingsConstructionAndLayoutMs }) }
        )
        $observations = @()
        foreach ($metric in $metrics) {
            $baseMedian = Get-Quantile $metric.baseline .5
            $candidateMedian = Get-Quantile $metric.candidate .5
            $baseP95 = Get-Quantile $metric.baseline .95
            $candidateP95 = Get-Quantile $metric.candidate .95
            $observations += [pscustomobject]@{
                metric=$metric.name; unit=$metric.unit
                baselineSamples=$metric.baseline.Count; candidateSamples=$metric.candidate.Count
                baselineMedian=$baseMedian; candidateMedian=$candidateMedian; medianDelta=$candidateMedian-$baseMedian
                baselineP95=$baseP95; candidateP95=$candidateP95; p95Delta=$candidateP95-$baseP95
            }
            if ($metric.name -eq 'privateBytes' -and $candidateP95-$baseP95 -gt 10MB) {
                $investigations += "Rows ${count}: private-byte p95 delta $([Math]::Round(($candidateP95-$baseP95)/1MB,2)) MiB exceeds the 10 MiB investigation threshold; inspect raw paired runs and repeat before making a performance claim."
            }
        }
        $pairs = @()
        for ($repetition = 1; $repetition -le 3; $repetition++) {
            $baseRun = @($base | Where-Object { $_.repetition -eq $repetition })[0]
            $candidateRun = @($candidate | Where-Object { $_.repetition -eq $repetition })[0]
            $baseP95 = Get-Quantile $baseRun.resources.privateBytes .95
            $candidateP95 = Get-Quantile $candidateRun.resources.privateBytes .95
            $pairs += [pscustomobject]@{
                repetition=$repetition; baselineRawFile=$baseRun.rawFile; candidateRawFile=$candidateRun.rawFile
                baselinePrivateBytesP95=$baseP95; candidatePrivateBytesP95=$candidateP95
                privateBytesP95Delta=$candidateP95-$baseP95
            }
        }
        $comparisons += [pscustomobject]@{
            rowCount=$count; baselineRuns=$base.Count; candidateRuns=$candidate.Count; pairs=$pairs
            baselineMaximumContainers=($base.maximumRealizedContainers | Measure-Object -Maximum).Maximum
            candidateMaximumContainers=($candidate.maximumRealizedContainers | Measure-Object -Maximum).Maximum
            metrics=$observations
        }
    }
    [pscustomobject]@{
        schemaVersion=1; status=$Status; failure=$Failure; evidenceClass=$manifest.evidenceClass
        baselineRevision=$baselineRevision; candidateRevision=$candidateRevision
        completedRuns=$runs.Count; expectedRuns=$sequence.Count; comparisons=$comparisons; investigations=$investigations
        interpretation='Descriptive same-runner synthetic comparison. Pooled samples are correlated; this is not statistical significance, normal app idle, gameplay FPS or hardware acceptance. Baseline/candidate raw files and run order are retained.'
    } | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'comparison.json') -Encoding utf8
    foreach ($note in $investigations) { Write-Warning $note }
}

try {
    foreach ($variant in @('baseline','candidate')) {
        $project = Join-Path $checkouts[$variant] 'tools/Flick.UiBenchmark/Flick.UiBenchmark.csproj'
        $binary = Join-Path $buildRoot $variant
        $log = Join-Path $OutputDirectory ("build-$variant.log")
        & dotnet build $project -c Release -o $binary -p:TreatWarningsAsErrors=true *> $log
        if ($LASTEXITCODE -ne 0) { throw "Benchmark build failed for $variant; see $log" }
        $executables[$variant] = Join-Path $binary 'Flick.UiBenchmark.dll'
    }
    foreach ($entry in $sequence) {
        $path = Join-Path $OutputDirectory $entry.file
        Write-Host ("Synthetic WPF diagnostic {0}/{1}: {2}, rows {3}, repetition {4}" -f $entry.ordinal,$sequence.Count,$entry.variant,$entry.rowCount,$entry.repetition)
        $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.WorkingDirectory = $checkouts[$entry.variant]
        foreach ($argument in @($executables[$entry.variant],$path,$entry.variant,$entry.revision,[string]$entry.rowCount,[string]$SampleSeconds)) {
            $info.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::Start($info)
        if ($null -eq $process) { throw 'Could not start the owned benchmark child process.' }
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (!$process.WaitForExit(($SampleSeconds+150)*1000)) {
                $process.Kill($true)
                $process.WaitForExit(5000) | Out-Null
                throw "Owned benchmark child exceeded its bounded timeout: $($entry.file)"
            }
            $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath ($path+'.stdout.log') -Encoding utf8
            $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath ($path+'.stderr.log') -Encoding utf8
            if ($process.ExitCode -ne 0) { throw "Synthetic benchmark invariant failed: $($entry.file)" }
            if (!(Test-Path -LiteralPath $path)) { throw 'Benchmark child returned without raw evidence.' }
        }
        finally { $process.Dispose() }
    }
    Write-Comparison 'completed'
    Write-Host 'Matched synthetic diagnostics completed. Review comparison.json alongside raw runs; physical and normal-app qualification remain separate.'
}
catch {
    Write-Comparison 'incomplete' $_.Exception.Message
    throw
}
