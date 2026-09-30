param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'BrowserQaConnection.ps1')
$manifest=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
if($manifest.RunId -notmatch '^[a-f0-9]{32}$' -or $manifest.Database -cne "ERP_KHO_BrowserQA_$($manifest.RunId)" -or $manifest.MarkerType -cne 'LocalBrowserFullStackQA'){throw 'Browser QA manifest validation failed.'}
foreach($prefix in @('Api','Frontend')) {
    $id=$manifest.($prefix+'Pid')
    if($null -eq $id -or [int]$id -le 0){continue}
    $process=Get-Process -Id $id -ErrorAction SilentlyContinue
    if($process) {
        $owned=Get-CimInstance Win32_Process -Filter "ProcessId=$id"
        if (!$manifest.($prefix+'StartTimeUtc') -or $process.StartTime.ToUniversalTime().Ticks -ne ([datetime]$manifest.($prefix+'StartTimeUtc')).ToUniversalTime().Ticks -or $owned.CommandLine -cne $manifest.($prefix+'CommandLine') -or $owned.ExecutablePath -cne $manifest.($prefix+'ExecutablePath')) { throw "PID $id ownership mismatch; cleanup refused." }
        Stop-Process -Id $id
        Wait-Process -Id $id -Timeout 10 -ErrorAction SilentlyContinue
    }
}
$master=Get-BrowserQaConnection 'master'
$databaseConnection=Get-BrowserQaConnection $manifest.Database
$connection=New-Object System.Data.SqlClient.SqlConnection $master
try {
    $connection.Open(); $exists=$connection.CreateCommand(); $exists.CommandText='SELECT COUNT(*) FROM sys.databases WHERE name=@database'; [void]$exists.Parameters.AddWithValue('@database',$manifest.Database)
    $databaseExists=[int]$exists.ExecuteScalar() -eq 1
} finally { $connection.Dispose() }
if ($databaseExists) {
$connection=New-Object System.Data.SqlClient.SqlConnection $databaseConnection
try{$connection.Open();$verify=$connection.CreateCommand();$verify.CommandText="SELECT COUNT(*) FROM dbo.__ERP_KHO_BrowserQAOwnership WHERE RunId=@run AND DatabaseName=@database AND MarkerType='LocalBrowserFullStackQA' AND DB_NAME()=@database";[void]$verify.Parameters.AddWithValue('@run',$manifest.RunId);[void]$verify.Parameters.AddWithValue('@database',$manifest.Database);if([int]$verify.ExecuteScalar()-ne 1){throw 'Browser QA database ownership mismatch; cleanup refused.'}}finally{$connection.Dispose()}
$connection=New-Object System.Data.SqlClient.SqlConnection $master
try{$connection.Open();$quoted='['+$manifest.Database.Replace(']',']]')+']';$drop=$connection.CreateCommand();$drop.CommandText="ALTER DATABASE $quoted SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $quoted";[void]$drop.ExecuteNonQuery();$absent=$connection.CreateCommand();$absent.CommandText='SELECT COUNT(*) FROM sys.databases WHERE name=@database';[void]$absent.Parameters.AddWithValue('@database',$manifest.Database);if([int]$absent.ExecuteScalar()-ne 0){throw 'Browser QA database cleanup verification failed.'}}finally{$connection.Dispose()}
}
Remove-Item -LiteralPath (Join-Path $manifest.ArtifactRoot 'credential.dpapi') -ErrorAction SilentlyContinue
Write-Output "Browser QA cleanup complete: processes stopped; database absent; credential removed."
