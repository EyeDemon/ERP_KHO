# Lifecycle records deliberately contain scalar process metadata only, never command lines or HTTP bodies.
function Write-BrowserQaLifecycle($Manifest, [string]$Event, [string]$Source, [hashtable]$Data = @{}) {
    if ($Manifest.RunId -notmatch '^[a-f0-9]{32}$' -or $Manifest.Database -cne "ERP_KHO_BrowserQA_$($Manifest.RunId)" -or $Manifest.MarkerType -cne 'LocalBrowserFullStackQA') { throw 'Browser QA lifecycle ownership validation failed.' }
    $record = [ordered]@{ AtUtc=[DateTime]::UtcNow.ToString('o'); RunId=$Manifest.RunId; Event=$Event; Source=$Source; ObserverPid=$PID }
    foreach ($key in $Data.Keys) {
        if ($key -notin @('Prefix','Pid','ParentPid','Executable','StartedAtUtc','State','Health','ExitCode','Reason','FailureType')) { throw 'Browser QA lifecycle field is not allowlisted.' }
        $record[$key] = $Data[$key]
    }
    try { $record | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $Manifest.ArtifactRoot 'lifecycle.jsonl') -Encoding utf8 }
    catch { Write-Warning 'Browser QA lifecycle record could not be written; diagnostic evidence is incomplete.' }
}

function Get-BrowserQaOwnedProcess($Manifest, [ValidateSet('Api','Frontend')][string]$Prefix) {
    $id = $Manifest.($Prefix+'Pid')
    if ($null -eq $id -or [int]$id -le 0) { return $null }
    $process = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($process) {
        $owned = Get-CimInstance Win32_Process -Filter "ProcessId=$id"
        if (!$Manifest.($Prefix+'StartTimeUtc') -or $process.StartTime.ToUniversalTime().Ticks -ne ([datetime]$Manifest.($Prefix+'StartTimeUtc')).ToUniversalTime().Ticks -or $owned.CommandLine -cne $Manifest.($Prefix+'CommandLine') -or $owned.ExecutablePath -cne $Manifest.($Prefix+'ExecutablePath')) { throw "PID $id ownership mismatch; operation refused." }
    }
    return $process
}

function Wait-BrowserQaHealthy($Manifest, [ValidateSet('Api','Frontend')][string]$Prefix) {
    $url = if ($Prefix -eq 'Api') { 'http://127.0.0.1:5265/swagger/index.html' } else { 'http://127.0.0.1:4175/' }
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        if (!(Get-BrowserQaOwnedProcess $Manifest $Prefix)) {
            Write-BrowserQaLifecycle $Manifest 'ProcessObserved' 'StartupHealth' @{Prefix=$Prefix;Pid=$Manifest.($Prefix+'Pid');State='Absent';ExitCode=$null}
            throw "Browser QA $Prefix process ended before readiness; exit code unknown."
        }
        try { $status=(Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2 -MaximumRedirection 0).StatusCode } catch { $status=$null }
        if ($status -eq 200) {
            Write-BrowserQaLifecycle $Manifest 'HealthObserved' 'StartupHealth' @{Prefix=$Prefix;Health='Healthy';State='Running'}
            return
        }
        Start-Sleep -Milliseconds 200
    }
    Write-BrowserQaLifecycle $Manifest 'HealthObserved' 'StartupHealth' @{Prefix=$Prefix;Health='Timeout';State='Running'}
    throw "Browser QA $Prefix did not become healthy."
}

# Read-only observation for an active run; this does not supervise, restart or stop processes.
function Get-BrowserQaLifecycleStatus($Manifest) {
    foreach ($prefix in @('Api','Frontend')) {
        $process=Get-BrowserQaOwnedProcess $Manifest $prefix
        $state=if ($process) { 'Running' } else { 'Absent' }
        $health='NotChecked'
        if ($process) {
            $url=if ($prefix -eq 'Api') { 'http://127.0.0.1:5265/swagger/index.html' } else { 'http://127.0.0.1:4175/' }
            try { $health=if ((Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2 -MaximumRedirection 0).StatusCode -eq 200) { 'Healthy' } else { 'Unhealthy' } } catch { $health='Unreachable' }
        }
        $data=@{Prefix=$prefix;Pid=$Manifest.($prefix+'Pid');State=$state;Health=$health;ExitCode=$null}
        Write-BrowserQaLifecycle $Manifest 'ProcessObserved' 'StatusCheck' $data
        [pscustomobject]$data
    }
}

function Stop-BrowserQaOwnedProcesses($Manifest) {
    foreach ($prefix in @('Api','Frontend')) {
        $process=Get-BrowserQaOwnedProcess $Manifest $prefix
        if (!$process) {
            Write-BrowserQaLifecycle $Manifest 'ProcessObserved' 'Stop-BrowserQa' @{Prefix=$prefix;Pid=$Manifest.($prefix+'Pid');State='Absent';ExitCode=$null}
            continue
        }
        Write-BrowserQaLifecycle $Manifest 'ProcessStopRequested' 'Stop-BrowserQa' @{Prefix=$prefix;Pid=$process.Id;State='Running'}
        Stop-Process -Id $process.Id
        Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
        $process.Refresh()
        $exitCode=$null
        if ($process.HasExited) { try { $exitCode=$process.ExitCode } catch { $exitCode=$null } }
        Write-BrowserQaLifecycle $Manifest 'ProcessObserved' 'Stop-BrowserQa' @{Prefix=$prefix;Pid=$process.Id;State=$(if ($process.HasExited) { 'Exited' } else { 'StopTimeout' });ExitCode=$exitCode}
        if (!$process.HasExited) { throw 'Owned Browser QA process did not stop within the cleanup deadline.' }
    }
}
