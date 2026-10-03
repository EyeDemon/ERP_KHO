function Get-BrowserQaConnection([string]$Database) {
    if ($Database -ne 'master' -and $Database -notmatch '^ERP_KHO_BrowserQA_[a-f0-9]{32}$') { throw 'Invalid owned Browser QA database name.' }
    $source = [Environment]::GetEnvironmentVariable('ERP_KHO_SQLSERVER_ADMIN_CONNECTION','Process')
    if ([string]::IsNullOrWhiteSpace($source)) { throw 'ERP_KHO_SQLSERVER_ADMIN_CONNECTION is not configured.' }
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $source
    if (!$builder.IntegratedSecurity -or $builder.UserID -or $builder.Password -or $builder.AttachDBFilename -or $builder.UserInstance) { throw 'Browser QA requires credential-free Integrated Security without attachment overrides.' }
    $server = $builder.DataSource -replace '^(?i)tcp:', ''
    $hostName = ($server -split '[\\,]')[0]
    if ($hostName -notin @('.', 'localhost', '127.0.0.1', '(local)', '(localdb)', [Environment]::MachineName)) { throw 'Browser QA requires a local SQL Server.' }
    $builder['Initial Catalog'] = $Database
    return $builder.ConnectionString
}
