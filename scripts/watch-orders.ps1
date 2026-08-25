$folder = 'd:\ERP_KHO\.agent\orders\ready'
if (!(Test-Path $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }
$watcher = New-Object IO.FileSystemWatcher $folder, '*.*' -Property @{
    IncludeSubdirectories = $false
    EnableRaisingEvents = $true
}
$action = {
    $path = $Event.SourceEventArgs.FullPath
    $changeType = $Event.SourceEventArgs.ChangeType
    Write-Host "NEW_ORDER_READY: $path"
}
Register-ObjectEvent $watcher 'Created' -Action $action | Out-Null
Register-ObjectEvent $watcher 'Renamed' -Action $action | Out-Null
Write-Host "Watching $folder for new orders..."
while ($true) { Start-Sleep -Seconds 1 }
