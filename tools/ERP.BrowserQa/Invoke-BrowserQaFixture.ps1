param([Parameter(Mandatory)][string]$ManifestPath, [ValidateSet('Sql','Credential','HoldLock','HoldExportLocations','HoldExportReceipt','Processes')][string]$Mode='Sql')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'BrowserQaConnection.ps1')
$manifest=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
if($manifest.RunId -notmatch '^[a-f0-9]{32}$' -or $manifest.Database -cne "ERP_KHO_BrowserQA_$($manifest.RunId)" -or $manifest.MarkerType -cne 'LocalBrowserFullStackQA') { throw 'Invalid owned Browser QA manifest.' }
$expectedRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../TestResults/BrowserQA/$($manifest.RunId)"))
if([IO.Path]::GetFullPath($manifest.ArtifactRoot) -cne $expectedRoot -or [IO.Path]::GetFullPath($ManifestPath) -cne (Join-Path $expectedRoot 'manifest.json')) { throw 'Browser QA artifact ownership mismatch.' }
$connection=New-Object System.Data.SqlClient.SqlConnection (Get-BrowserQaConnection $manifest.Database)
$stage='ownership'
try {
    $connection.Open()
    $verify=$connection.CreateCommand()
    $verify.CommandText="SELECT COUNT(*) FROM dbo.__ERP_KHO_BrowserQAOwnership WHERE RunId=@run AND DatabaseName=@database AND MarkerType='LocalBrowserFullStackQA' AND DB_NAME()=@database"
    [void]$verify.Parameters.AddWithValue('@run',$manifest.RunId);[void]$verify.Parameters.AddWithValue('@database',$manifest.Database)
    if([int]$verify.ExecuteScalar() -ne 1) { throw 'Browser QA database ownership mismatch.' }
    if($Mode -eq 'Processes') {
        $records=@()
        foreach($processId in @($manifest.BrowserRunner.Pid,$manifest.BrowserRunner.ServerPid)) {
            $process=Get-Process -Id $processId -ErrorAction Stop
            $details=Get-CimInstance Win32_Process -Filter "ProcessId=$processId"
            $records += [ordered]@{Pid=$processId;StartTimeUtc=$process.StartTime.ToUniversalTime().ToString('o');CommandLine=$details.CommandLine;ExecutablePath=$details.ExecutablePath;ParentProcessId=$details.ParentProcessId}
        }
        [Console]::Out.Write(($records|ConvertTo-Json -Compress))
    } elseif($Mode -eq 'Credential') {
        $stage='credential'
        # Returned only to the runner's captured pipe; never persisted as plaintext or logged.
        $encrypted=(Get-Content -LiteralPath (Join-Path $expectedRoot 'credential.dpapi') -Raw).Trim()
        $secure=ConvertTo-SecureString $encrypted
        $stage='credential-marshal'
        $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try {[Console]::Out.Write([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer))}
        finally {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)}
    } elseif($Mode -in @('HoldLock','HoldExportLocations','HoldExportReceipt')) {
        $stage='lock'
        $transaction=$connection.BeginTransaction()
        try {
            $command=$connection.CreateCommand();$command.Transaction=$transaction
            $command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'ERP.PermissionAdministration',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @result<0 THROW 51009,'QA lock unavailable.',1;"
            if($Mode -ne 'HoldLock') {
                $request=[Console]::In.ReadToEnd()|ConvertFrom-Json
                if(($request.id -isnot [int] -and $request.id -isnot [long]) -or $request.id -le 0 -or $request.id -gt [int]::MaxValue) { throw 'Positive fixture ID required.' }
                $command.CommandText=if($Mode -eq 'HoldExportReceipt') {'SELECT Id FROM ExportReceipts WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id'} else {'SELECT * FROM WarehouseLocations WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseId=@id ORDER BY Id'}
                [void]$command.Parameters.AddWithValue('@id',$request.id)
            }
            [void]$command.ExecuteNonQuery()
            [Console]::Out.WriteLine('LOCK_READY');[Console]::Out.Flush()
            Start-Sleep -Milliseconds 2000
            $transaction.Commit()
        } finally {$transaction.Dispose()}
    } else {
        $stage='fixture-or-postcondition'
        $request=[Console]::In.ReadToEnd()|ConvertFrom-Json
        $command=$connection.CreateCommand();$command.CommandTimeout=30;$command.CommandText=$request.sql
        if($request.parameters) {foreach($property in $request.parameters.PSObject.Properties) {[void]$command.Parameters.AddWithValue('@'+$property.Name,$property.Value)}}
        # Fixtures and postconditions only. Callers return one JSON value, not arbitrary response/secret dumps.
        $result=$command.ExecuteScalar()
        if($null -eq $result -or $result -is [DBNull]) {[Console]::Out.Write('{}')} else {[Console]::Out.Write([string]$result)}
    }
} catch {
    $number=0; $cursor=$_.Exception
    while($cursor) {if($cursor -is [System.Data.SqlClient.SqlException]) {$number=$cursor.Number;break};$cursor=$cursor.InnerException}
    $kind=$_.Exception.GetBaseException().GetType().Name
    $line=$_.InvocationInfo.ScriptLineNumber
    throw "Owned BrowserQA helper failed; stage=$stage; sqlNumber=$number; kind=$kind; line=$line."
} finally {$connection.Dispose()}
