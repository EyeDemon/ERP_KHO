# No database, process, listener or credential is created. Exercise the real lifecycle functions with in-memory command doubles.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'BrowserQaLifecycle.ps1')
$startPath=Join-Path $PSScriptRoot 'Start-BrowserQa.ps1'
$tokens=$null; $parseErrors=$null
$startup=[Management.Automation.Language.Parser]::ParseFile($startPath,[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count) { throw 'Startup helper parse failed.' }
$outerTry=$startup.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.TryStatementAst] } | Select-Object -Last 1
$catchText=$outerTry.CatchClauses[0].Body.Extent.Text
$startupCatch=[scriptblock]::Create($catchText.Substring(1,$catchText.Length-2))
$script:passed=0
function Assert($Condition,[string]$Message) { if (!$Condition) { throw $Message } }
function Expect-Failure([scriptblock]$Action,[string]$Message) {
    $failed=$false
    try { & $Action } catch { $failed=$true; Assert ($_.Exception.Message -like $Message) 'Unexpected error.' }
    Assert $failed 'Expected failure was not propagated.'
}
function Reset-State {
    $script:events=@(); $script:stopped=@(); $script:healthCalls=0; $script:status=200; $script:absent=$false; $script:cleanupThrows=$false; $script:logFails=$false
    $script:process=[pscustomobject]@{Id=12345;StartTime=[datetime]'2026-10-11T00:00:00Z';HasExited=$false;ExitCode=$null}
    $script:process | Add-Member -MemberType ScriptMethod -Name Refresh -Value {}
    $script:owned=[pscustomobject]@{CommandLine='synthetic fixture';ExecutablePath='C:\test\dotnet.exe'}
    $script:manifest=[pscustomobject]@{RunId='0123456789abcdef0123456789abcdef';Database='ERP_KHO_BrowserQA_0123456789abcdef0123456789abcdef';MarkerType='LocalBrowserFullStackQA';ArtifactRoot='not-written';ApiPid=12345;ApiStartTimeUtc='2026-10-11T00:00:00Z';ApiCommandLine='synthetic fixture';ApiExecutablePath='C:\test\dotnet.exe';FrontendPid=$null}
}
function Add-Content { param([Parameter(ValueFromPipeline)]$Value,$LiteralPath,$Encoding) process { if ($script:logFails) { throw 'simulated diagnostic write failure' }; $script:events+=($Value|ConvertFrom-Json) } }
function Get-Process { param($Id,$ErrorAction) if (!$script:absent) { $script:process } }
function Get-CimInstance { param($ClassName,$Filter) $script:owned }
function Stop-Process { param($Id) $script:stopped+=$Id; $script:absent=$true; $script:process.HasExited=$true; $script:process.ExitCode=137 }
function Wait-Process { param($Id,$Timeout,$ErrorAction) Assert ($Timeout -eq 10) 'Cleanup wait must be bounded.' }
function Start-Sleep { param($Milliseconds) }
function Invoke-WebRequest { param($Uri,[switch]$UseBasicParsing,$TimeoutSec,$MaximumRedirection) $script:healthCalls++; Assert ($TimeoutSec -eq 2 -and $MaximumRedirection -eq 0) 'Health request must be bounded and not redirect.'; [pscustomobject]@{StatusCode=$script:status} }
function Stop-BrowserQaMock { param($ManifestPath,$Reason) Assert ($Reason -eq 'StartupFailure') 'Startup cleanup reason lost.'; Stop-BrowserQaOwnedProcesses $script:manifest; if ($script:cleanupThrows) { throw 'simulated cleanup failure' } }
function Join-Path { param($Path,$ChildPath) if ($ChildPath -eq 'Stop-BrowserQa.ps1') { 'Stop-BrowserQaMock' } else { "$Path/$ChildPath" } }
function Check([string]$Name,[scriptblock]$Action) { Reset-State; & $Action; $script:passed++; Write-Output "PASS $Name" }

Check 'ready helper does not request cleanup on success' {
    Wait-BrowserQaHealthy $manifest Api
    Assert ($stopped.Count -eq 0 -and $events[0].Event -eq 'HealthObserved') 'Readiness must not stop processes.'
    Assert ($null -eq $outerTry.Finally) 'Startup success must not execute cleanup finally.'
    Assert ($outerTry.Body.Extent.Text -match "StartupReady") 'Startup readiness must be recorded.'
}
Check 'health timeout propagates with diagnostic' {
    $script:status=503
    Expect-Failure { Wait-BrowserQaHealthy $manifest Api } '*did not become healthy*'
    Assert ($healthCalls -eq 30 -and $events[-1].Health -eq 'Timeout') 'Timeout diagnostic missing.'
}
Check 'process gone before readiness has unknown exit code' {
    $script:absent=$true
    Expect-Failure { Wait-BrowserQaHealthy $manifest Api } '*ended before readiness*'
    Assert ($healthCalls -eq 0 -and $events[0].State -eq 'Absent' -and $null -eq $events[0].ExitCode) 'Cannot invent exit code for absent process.'
}
Check 'startup failure keeps original error and requests owned cleanup' {
    $createdDatabase=$true; $manifestPath='not-written'
    Expect-Failure { try { throw 'original startup failure' } catch { & $startupCatch } } 'original startup failure'
    Assert ($stopped.Count -eq 1 -and $events[0].Event -eq 'StartupFailed') 'Startup error cleanup was not invoked.'
}
Check 'cleanup failure cannot replace startup failure' {
    $createdDatabase=$true; $manifestPath='not-written'; $script:cleanupThrows=$true
    $WarningPreference='SilentlyContinue'
    Expect-Failure { try { throw 'original startup failure' } catch { & $startupCatch } } 'original startup failure'
}
Check 'startup failure before database creation never requests cleanup' {
    $createdDatabase=$false; $manifestPath='not-written'
    Expect-Failure { try { throw 'original startup failure' } catch { & $startupCatch } } 'original startup failure'
    Assert ($stopped.Count -eq 0) 'Cannot cleanup resources not created by startup.'
}
Check 'diagnostic write failure cannot prevent owned cleanup' {
    $script:logFails=$true; $WarningPreference='SilentlyContinue'
    Stop-BrowserQaOwnedProcesses $manifest
    Assert ($stopped.Count -eq 1) 'Diagnostic failure blocked cleanup.'
}
Check 'reused PID is never stopped' {
    $script:process.StartTime=$process.StartTime.AddSeconds(1)
    Expect-Failure { Stop-BrowserQaOwnedProcesses $manifest } '*ownership mismatch*'
    Assert ($stopped.Count -eq 0) 'Reused PID stopped.'
}
Check 'executable mismatch is never stopped' {
    $script:owned.ExecutablePath='C:\other\dotnet.exe'
    Expect-Failure { Stop-BrowserQaOwnedProcesses $manifest } '*ownership mismatch*'
    Assert ($stopped.Count -eq 0) 'Unknown executable stopped.'
}
Check 'command-line mismatch is never stopped' {
    $script:owned.CommandLine='other run'
    Expect-Failure { Stop-BrowserQaOwnedProcesses $manifest } '*ownership mismatch*'
    Assert ($stopped.Count -eq 0) 'Unknown command stopped.'
}
Check 'repeated process cleanup is safe and records observed exit code' {
    Stop-BrowserQaOwnedProcesses $manifest
    Stop-BrowserQaOwnedProcesses $manifest
    Assert ($stopped.Count -eq 1) 'Duplicate stop effect.'
    Assert (($events|Where-Object ExitCode -eq 137).Count -eq 1) 'Observed exit code missing.'
    Assert ($events[-2].State -eq 'Absent') 'Second cleanup must record absent process.'
}
Check 'status reports lost environment without restart or HTTP' {
    $script:absent=$true
    $observed=@(Get-BrowserQaLifecycleStatus $manifest)
    Assert ($observed.Count -eq 2 -and $healthCalls -eq 0 -and $stopped.Count -eq 0) 'Status must be observation only.'
}
Check 'diagnostic rejects fields outside scalar allowlist' {
    Expect-Failure { Write-BrowserQaLifecycle $manifest Test Test @{Token='synthetic-do-not-write'} } '*not allowlisted*'
    Assert ($events.Count -eq 0) 'Sensitive field recorded.'
}
Write-Output "Browser QA lifecycle $passed/$passed PASS (command doubles; no QA resources)."
