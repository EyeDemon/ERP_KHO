param([string]$RunId = ([guid]::NewGuid().ToString('N')))
$ErrorActionPreference = 'Stop'
if ($RunId -notmatch '^[a-f0-9]{32}$') { throw 'Invalid Browser QA Run ID.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$database = "ERP_KHO_BrowserQA_$RunId"
$artifactRoot = Join-Path $repo "TestResults/BrowserQA/$RunId"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$master = 'Server=localhost\SQLEXPRESS;Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5'
$connection = New-Object System.Data.SqlClient.SqlConnection $master
try {
    $connection.Open()
    $check = $connection.CreateCommand(); $check.CommandText = 'SELECT COUNT(*) FROM sys.databases WHERE name=@name'
    [void]$check.Parameters.AddWithValue('@name', $database)
    if ([int]$check.ExecuteScalar() -ne 0) { throw 'Exact Browser QA database already exists.' }
    $create = $connection.CreateCommand(); $create.CommandText = 'CREATE DATABASE [' + $database + ']'; [void]$create.ExecuteNonQuery()
} finally { $connection.Dispose() }

$databaseConnection = "Server=localhost\SQLEXPRESS;Initial Catalog=$database;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5"
$connection = New-Object System.Data.SqlClient.SqlConnection $databaseConnection
try {
    $connection.Open(); $marker = $connection.CreateCommand(); $marker.CommandText = "CREATE TABLE dbo.__ERP_KHO_BrowserQAOwnership(RunId char(32) NOT NULL PRIMARY KEY,DatabaseName sysname NOT NULL,MarkerType nvarchar(64) NOT NULL,CreatedAtUtc datetime2 NOT NULL); INSERT dbo.__ERP_KHO_BrowserQAOwnership VALUES(@run,@database,'LocalBrowserFullStackQA',SYSUTCDATETIME())"
    [void]$marker.Parameters.AddWithValue('@run',$RunId); [void]$marker.Parameters.AddWithValue('@database',$database); [void]$marker.ExecuteNonQuery()
} finally { $connection.Dispose() }
$manifestPath = Join-Path $artifactRoot 'manifest.json'
$manifest=[ordered]@{RunId=$RunId;Database=$database;MarkerType='LocalBrowserFullStackQA';ApiPid=$null;FrontendPid=$null;ApiUrl='http://127.0.0.1:5265';FrontendUrl='http://127.0.0.1:4175';ArtifactRoot=$artifactRoot;CreatedAtUtc=[DateTime]::UtcNow.ToString('o')}
$manifest|ConvertTo-Json|Set-Content $manifestPath -Encoding UTF8
$env:ConnectionStrings__DefaultConnection = $databaseConnection
try { dotnet ef database update --project "$repo/ERP.Infrastructure/ERP.Infrastructure.csproj" --startup-project "$repo/ERP.Api/ERP.Api.csproj" --configuration Release --no-build }
finally { Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue }
if ($LASTEXITCODE -ne 0) { throw 'EF migration failed.' }

$salt = New-Object byte[] 16; $rng=[Security.Cryptography.RandomNumberGenerator]::Create(); try{$rng.GetBytes($salt)}finally{$rng.Dispose()}
$password = 'Qa!' + $RunId.Substring(0,12) + 'Z9'
$pbkdf2=New-Object Security.Cryptography.Rfc2898DeriveBytes($password,$salt,100000,[Security.Cryptography.HashAlgorithmName]::SHA256); try{$derived=$pbkdf2.GetBytes(32)}finally{$pbkdf2.Dispose()}
$passwordHash = ([BitConverter]::ToString($derived).Replace('-',''))+':'+([BitConverter]::ToString($salt).Replace('-',''))+':100000:SHA256'
$connection = New-Object System.Data.SqlClient.SqlConnection $databaseConnection
try {
    $connection.Open(); $command = $connection.CreateCommand(); $command.CommandText = @'
INSERT dbo.Roles(RoleName,Description) VALUES('Admin',N'Synthetic browser QA'),('Manager',N'Synthetic browser QA'),('WarehouseStaff',N'Synthetic browser QA'),('Viewer',N'Synthetic browser QA');
DECLARE @admin int=(SELECT Id FROM dbo.Roles WHERE RoleName='Admin'), @manager int=(SELECT Id FROM dbo.Roles WHERE RoleName='Manager'), @viewer int=(SELECT Id FROM dbo.Roles WHERE RoleName='Viewer');
INSERT dbo.Users(Username,PasswordHash,FullName,RoleId,IsActive,CreatedAt,FailedLoginCount) VALUES('qa_admin_browser',@hash,N'QA Admin',@admin,1,SYSUTCDATETIME(),0),('qa_manager_browser',@hash,N'QA Manager',@manager,1,SYSUTCDATETIME(),0),('qa_viewer_browser',@hash,N'QA Viewer',@viewer,1,SYSUTCDATETIME(),0);
INSERT dbo.Warehouses(Code,Name,Address,IsActive,CreatedAt) VALUES('QA-WH01',N'Kho browser QA',N'Loopback synthetic target',1,SYSUTCDATETIME());
DECLARE @warehouse int=(SELECT Id FROM dbo.Warehouses WHERE Code='QA-WH01'), @adminUser int=(SELECT Id FROM dbo.Users WHERE Username='qa_admin_browser'), @managerUser int=(SELECT Id FROM dbo.Users WHERE Username='qa_manager_browser'), @viewerUser int=(SELECT Id FROM dbo.Users WHERE Username='qa_viewer_browser');
INSERT dbo.UserWarehouses(UserId,WarehouseId,CreatedAt,CreatedBy) VALUES(@managerUser,@warehouse,SYSUTCDATETIME(),@adminUser),(@viewerUser,@warehouse,SYSUTCDATETIME(),@adminUser);
INSERT dbo.Units(Code,Name,IsActive,CreatedAt) VALUES('EA',N'Each',1,SYSUTCDATETIME());
INSERT dbo.Products(Code,Name,Description,UnitId,IsActive,CreatedAt) VALUES('LOOKUP-COLLIDE',N'Product code collision',NULL,(SELECT Id FROM dbo.Units WHERE Code='EA'),1,SYSUTCDATETIME()),('BARCODE-TARGET',N'Barcode target',NULL,(SELECT Id FROM dbo.Units WHERE Code='EA'),1,SYSUTCDATETIME());
INSERT dbo.ProductBarcodes(ProductId,Value) VALUES((SELECT Id FROM dbo.Products WHERE Code='BARCODE-TARGET'),'LOOKUP-COLLIDE');
INSERT dbo.InventoryStocks(ProductId,WarehouseId,Quantity,ReservedQuantity,LastUpdated) SELECT Id,@warehouse,100,0,SYSUTCDATETIME() FROM dbo.Products;
'@
    [void]$command.Parameters.AddWithValue('@hash',$passwordHash)
    [void]$command.ExecuteNonQuery()
    $verify=$connection.CreateCommand(); $verify.CommandText="SELECT CONCAT(DB_NAME(),'|',(SELECT COUNT(*) FROM dbo.__ERP_KHO_BrowserQAOwnership WHERE RunId=@run AND DatabaseName=@database AND MarkerType='LocalBrowserFullStackQA'),'|',(SELECT COUNT(*) FROM dbo.InventoryTransactions),'|',(SELECT COUNT(*) FROM dbo.InventoryStocks),'|',(SELECT COUNT(*) FROM dbo.StockReservations))"
    [void]$verify.Parameters.AddWithValue('@run',$RunId); [void]$verify.Parameters.AddWithValue('@database',$database)
    Write-Output ('Target verification db|marker|transactions|stocks|reservations: '+$verify.ExecuteScalar())
} finally { $connection.Dispose() }

$jwtBytes=New-Object byte[] 48; $rng=[Security.Cryptography.RandomNumberGenerator]::Create(); try{$rng.GetBytes($jwtBytes)}finally{$rng.Dispose()}; $jwt=[Convert]::ToBase64String($jwtBytes)
$variables=@{ DOTNET_ENVIRONMENT='Development'; ConnectionStrings__DefaultConnection=$databaseConnection; JwtSettings__Secret=$jwt; Cors__AllowedOrigins__0='http://127.0.0.1:4175' }
$previous=@{}; foreach($key in $variables.Keys){$previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$variables[$key],'Process')}
try { $api=Start-Process dotnet -ArgumentList @("$repo/ERP.Api/bin/Release/net10.0/ERP.Api.dll",'--contentRoot',"$repo/ERP.Api",'--urls','http://127.0.0.1:5265') -WorkingDirectory $artifactRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifactRoot 'api.stdout.log') -RedirectStandardError (Join-Path $artifactRoot 'api.stderr.log') -PassThru }
finally { foreach($key in $variables.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')} }
$oldApi=[Environment]::GetEnvironmentVariable('VITE_API_BASE_URL','Process'); [Environment]::SetEnvironmentVariable('VITE_API_BASE_URL','http://127.0.0.1:5265','Process')
try { $frontend=Start-Process node -ArgumentList @("$repo/frontend/node_modules/vite/bin/vite.js",'--host','127.0.0.1','--port','4175','--strictPort') -WorkingDirectory "$repo/frontend" -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifactRoot 'frontend.stdout.log') -RedirectStandardError (Join-Path $artifactRoot 'frontend.stderr.log') -PassThru }
finally { [Environment]::SetEnvironmentVariable('VITE_API_BASE_URL',$oldApi,'Process') }
$manifest.ApiPid=$api.Id; $manifest.FrontendPid=$frontend.Id
$manifest|ConvertTo-Json|Set-Content $manifestPath -Encoding UTF8
(ConvertTo-SecureString $password -AsPlainText -Force|ConvertFrom-SecureString)|Set-Content (Join-Path $artifactRoot 'credential.dpapi') -Encoding ascii
[Array]::Clear($jwtBytes,0,$jwtBytes.Length); $jwt=$null; $password=$null; $databaseConnection=$null
$manifest|ConvertTo-Json -Compress
