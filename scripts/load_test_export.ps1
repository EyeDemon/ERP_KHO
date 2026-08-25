param (
    [string]$Token,
    [string]$BaseUrl = "http://localhost:5000",
    [int[]]$ReceiptIds,
    [int]$ProductId = 1,
    [int]$WarehouseId = 1,
    [string]$ConnectionString
)

if (-not $Token) {
    Write-Host "Vui lòng truyền token JWT: .\load_test_export.ps1 -Token 'eyJ...'" -ForegroundColor Red
    exit
}

if ($ReceiptIds.Count -eq 0) {
    Write-Host "Vui lòng truyền danh sách ReceiptIds: -ReceiptIds 1,2,3" -ForegroundColor Red
    exit
}

if (-not $ConnectionString) {
    $ConnectionString = $env:ERP_KHO_CONNECTION_STRING
}

if (-not $ConnectionString) {
    Write-Host "Vui lòng truyền connection string qua tham số -ConnectionString hoặc biến môi trường ERP_KHO_CONNECTION_STRING." -ForegroundColor Red
    Write-Host "Ví dụ: .\load_test_export.ps1 -Token '...' -ReceiptIds 1,2 -ConnectionString 'Server=...'" -ForegroundColor Yellow
    exit
}

Write-Host "Bắt đầu load test duyệt nhiều phiếu xuất song song..." -ForegroundColor Cyan

# Lấy tồn kho trước khi test
$initialStockQuery = "SELECT Quantity FROM InventoryStocks WHERE ProductId = $ProductId AND WarehouseId = $WarehouseId"
$initialTxQuery = "SELECT COUNT(*) as Cnt FROM InventoryTransactions WHERE ProductId = $ProductId AND WarehouseId = $WarehouseId AND TransactionType = 2" # Export = 2

# Execute reader manually
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $ConnectionString
$connection.Open()
$cmd1 = $connection.CreateCommand()
$cmd1.CommandText = $initialStockQuery
$initialStock = $cmd1.ExecuteScalar()
if ($null -eq $initialStock) { $initialStock = 0 }
$cmd2 = $connection.CreateCommand()
$cmd2.CommandText = $initialTxQuery
$initialTxCount = $cmd2.ExecuteScalar()
$connection.Close()

Write-Host "Tồn kho ban đầu: $initialStock" -ForegroundColor Yellow
Write-Host "Số giao dịch xuất ban đầu: $initialTxCount" -ForegroundColor Yellow

# Define the script block to be run in parallel
$scriptBlock = {
    param($receiptId, $baseUrl, $token)
    
    $url = "$baseUrl/api/exportreceipts/$receiptId/approve"
    
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    }
    
    try {
        $startTime = Get-Date
        $response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -ErrorAction Stop
        $endTime = Get-Date
        $duration = ($endTime - $startTime).TotalMilliseconds
        return [PSCustomObject]@{
            ReceiptId = $receiptId
            Status = "Success"
            StatusCode = 200
            Message = "OK"
            DurationMs = $duration
        }
    } catch {
        $endTime = Get-Date
        $duration = ($endTime - $startTime).TotalMilliseconds
        
        $statusCode = 500
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        return [PSCustomObject]@{
            ReceiptId = $receiptId
            Status = "Failed"
            StatusCode = $statusCode
            Message = $_.Exception.Message
            DurationMs = $duration
        }
    }
}

# Run tasks in parallel using Start-Job
$jobs = @()
foreach ($id in $ReceiptIds) {
    $jobs += Start-Job -ScriptBlock $scriptBlock -ArgumentList $id, $BaseUrl, $Token
}

Write-Host "Đang chờ tất cả các request hoàn thành..." -ForegroundColor Yellow
Wait-Job -Job $jobs | Out-Null

$results = @()
foreach ($job in $jobs) {
    $results += Receive-Job -Job $job
    Remove-Job -Job $job
}

Write-Host "Kết quả:" -ForegroundColor Green
$results | Format-Table -AutoSize

$successCount = ($results | Where-Object { $_.Status -eq "Success" }).Count
$conflictCount = ($results | Where-Object { $_.StatusCode -eq 409 }).Count
$otherFailedCount = ($results | Where-Object { $_.Status -eq "Failed" -and $_.StatusCode -ne 409 }).Count

Write-Host "Tổng số request thành công: $successCount" -ForegroundColor Green
Write-Host "Tổng số request thất bại do 409 Conflict: $conflictCount" -ForegroundColor Yellow
Write-Host "Tổng số request thất bại do lỗi khác: $otherFailedCount" -ForegroundColor Red

# Lấy tồn kho sau khi test
$connection.Open()
$finalStock = $cmd1.ExecuteScalar()
if ($null -eq $finalStock) { $finalStock = 0 }
$finalTxCount = $cmd2.ExecuteScalar()
$connection.Close()

Write-Host "Tồn kho cuối cùng: $finalStock" -ForegroundColor Magenta
Write-Host "Số giao dịch xuất cuối cùng: $finalTxCount" -ForegroundColor Magenta

# Validate
if ($finalStock -lt 0) {
    Write-Host "FAIL: Tồn kho bị âm!" -ForegroundColor Red
} else {
    Write-Host "PASS: Tồn kho không bị âm." -ForegroundColor Green
}

$txCreated = $finalTxCount - $initialTxCount
if ($txCreated -eq $successCount) {
    Write-Host "PASS: Số giao dịch Export sinh ra ($txCreated) khớp với số phiếu duyệt thành công ($successCount)." -ForegroundColor Green
} else {
    Write-Host "FAIL: Số giao dịch Export sinh ra ($txCreated) KHÔNG khớp với số phiếu duyệt thành công ($successCount)." -ForegroundColor Red
}
