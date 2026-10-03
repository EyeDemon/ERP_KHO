$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $root
$line = dotnet user-secrets list --project ERP.Api | Where-Object { $_ -like 'ConnectionStrings:DefaultConnection = *' } | Select-Object -First 1
if (!$line) { throw 'Local source configuration unavailable.' }
$source = $line.Substring($line.IndexOf(' = ') + 3)
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($source)
if (!$builder.IntegratedSecurity -or $builder.InitialCatalog -ne 'ERP_KHO' -or !$builder.DataSource.StartsWith('(localdb)\')) { throw 'Read-only snapshot target refused.' }
function Get-SafetySnapshot {
    $connection = New-Object System.Data.SqlClient.SqlConnection($source)
    try {
        $connection.Open()
        function Read-Json([string]$sql) {
            $cmd = $connection.CreateCommand()
            $cmd.CommandText = $sql
            $cmd.CommandTimeout = 60
            $reader = $cmd.ExecuteReader()
            try { $text = New-Object Text.StringBuilder; while ($reader.Read()) { [void]$text.Append($reader.GetString(0)) }; return $text.ToString() }
            finally { $reader.Dispose(); $cmd.Dispose() }
        }
        $tables = Read-Json "SELECT s.name AS SchemaName,t.name AS TableName FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name FOR JSON PATH" | ConvertFrom-Json
        $hashes = @()
        foreach ($table in $tables) {
            $schema = $table.SchemaName.Replace(']', ']]'); $name = $table.TableName.Replace(']', ']]')
            # Canonicalize rows without retaining or reporting business values.
            $json = Read-Json "SELECT * FROM [$schema].[$name] FOR JSON PATH, INCLUDE_NULL_VALUES"
            $rows = @($json | ConvertFrom-Json | ForEach-Object { ConvertTo-Json -InputObject $_ -Depth 30 -Compress } | Sort-Object)
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join "`n")))).Replace('-', '') }
            finally { $sha.Dispose() }
            $hashes += [ordered]@{ Table = "$schema.$name"; Count = $rows.Count; SHA256 = $hash }
        }
        $state = Read-Json "SELECT state_desc,recovery_model_desc FROM sys.databases WHERE name='ERP_KHO' FOR JSON PATH"
        $history = Read-Json "SELECT backup_set_id,type,backup_start_date,backup_finish_date FROM msdb.dbo.backupset WHERE database_name='ERP_KHO' ORDER BY backup_set_id FOR JSON PATH"
        $remaining = Read-Json "SELECT name FROM sys.databases WHERE name LIKE 'ERP[_]KHO[_]Integration[_]%' ORDER BY name FOR JSON PATH"
    } finally { $connection.Dispose() }
    $files = @(Get-ChildItem -LiteralPath 'D:\ERP_KHO_Backups' -Recurse -File | Where-Object { $_.Extension -in '.bak','.trn' } | Sort-Object FullName | ForEach-Object { [ordered]@{Path=$_.FullName;Size=$_.Length;LastWriteUtc=$_.LastWriteTimeUtc.ToString('o')} })
    $tasks = @(Get-ScheduledTask | Sort-Object TaskPath,TaskName | ForEach-Object {
        $xml = Export-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($xml))).Replace('-','') } finally {$sha.Dispose()}
        [ordered]@{Name=$_.TaskPath+$_.TaskName;Enabled=$_.Settings.Enabled;DefinitionSHA256=$hash}
    })
    return [ordered]@{State=$state;Tables=$hashes;BackupHistory=$history;RemainingDatabases=$remaining;BackupFiles=$files;Tasks=$tasks}
}
$before = Get-SafetySnapshot
if ($before.RemainingDatabases -notin '', '[]') { throw 'An isolated database already exists; investigate before this run.' }
try {
    $env:ERP_KHO_SQLSERVER_ADMIN_CONNECTION=$source
    dotnet run --project tools/ERP.SqlIntegrationHarness/ERP.SqlIntegrationHarness.csproj --configuration Release --no-build
    $appExit=$LASTEXITCODE
    $env:ERP_KHO_APPROVAL_SQLSERVER_ADMIN_CONNECTION=$source
    dotnet test ERP.Api.Tests/ERP.Api.Tests.csproj --configuration Release --no-build --logger 'trx;LogFileName=api.trx' --results-directory TestResults/SqlIntegration/safety-verification-20260903
    $apiExit=$LASTEXITCODE
} finally {
    Remove-Item Env:\ERP_KHO_SQLSERVER_ADMIN_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:\ERP_KHO_APPROVAL_SQLSERVER_ADMIN_CONNECTION -ErrorAction SilentlyContinue
}
$after = Get-SafetySnapshot
$comparisons = [ordered]@{}
foreach ($key in $before.Keys) { $comparisons[$key] = (ConvertTo-Json -InputObject $before[$key] -Depth 40 -Compress) -ceq (ConvertTo-Json -InputObject $after[$key] -Depth 40 -Compress) }
$evidence = [ordered]@{CapturedAtUtc=[DateTime]::UtcNow.ToString('o');ApplicationExit=$appExit;ApiExit=$apiExit;Comparison=$comparisons;Before=$before;After=$after}
$output = Join-Path $root 'TestResults/SqlIntegration/safety-verification-20260903/safety-evidence.json'
[IO.File]::WriteAllText($output, (ConvertTo-Json -InputObject $evidence -Depth 40), (New-Object Text.UTF8Encoding($false)))
$comparisons | ConvertTo-Json
$source=$null; $line=$null; $builder=$null
if ($appExit -ne 0 -or $apiExit -ne 0 -or $comparisons.Values -contains $false) { exit 1 }
exit 0
