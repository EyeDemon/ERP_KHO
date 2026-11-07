$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'BrowserQaConnection.ps1')
$previous=[Environment]::GetEnvironmentVariable('ERP_KHO_SQLSERVER_ADMIN_CONNECTION','Process')
try {
    foreach($source in @('', 'Server=remote.invalid;Integrated Security=True', 'Server=localhost;User ID=example;Password=example', 'Server=localhost;Integrated Security=True;AttachDbFilename=C:\example.mdf')) {
        [Environment]::SetEnvironmentVariable('ERP_KHO_SQLSERVER_ADMIN_CONNECTION',$source,'Process')
        $rejected=$false
        try { [void](Get-BrowserQaConnection 'master') } catch { $rejected=$true }
        if (!$rejected) { throw 'Unsafe QA connection was accepted.' }
    }
    [Environment]::SetEnvironmentVariable('ERP_KHO_SQLSERVER_ADMIN_CONNECTION','Server=localhost;Integrated Security=True','Process')
    $connection=New-Object System.Data.SqlClient.SqlConnectionStringBuilder (Get-BrowserQaConnection 'ERP_KHO_BrowserQA_0123456789abcdef0123456789abcdef')
    if ($connection.InitialCatalog -cne 'ERP_KHO_BrowserQA_0123456789abcdef0123456789abcdef') { throw 'QA target override failed.' }
    $rejected=$false
    try { [void](Get-BrowserQaConnection 'ERP_KHO') } catch { $rejected=$true }
    if (!$rejected) { throw 'Business database target was accepted.' }
    Write-Output 'Browser QA connection guards PASS (no database access).'
} finally { [Environment]::SetEnvironmentVariable('ERP_KHO_SQLSERVER_ADMIN_CONNECTION',$previous,'Process') }
