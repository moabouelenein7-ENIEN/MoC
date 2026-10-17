param(
    [switch]$ForceKill = $false
)

if ($ForceKill) {
    Write-Host "Closing ETABS..." -ForegroundColor Yellow
    Get-Process -Name ETABS -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$etabs = Get-Process -Name ETABS -ErrorAction SilentlyContinue
if ($etabs) {
    Write-Host "ETABS (PID: $($etabs.Id)) is currently running. Waiting for you to close ETABS..." -ForegroundColor Cyan
    $etabs.WaitForExit()
    Write-Host "ETABS has closed!" -ForegroundColor Green
    Start-Sleep -Seconds 1
}

Write-Host "Deploying updated MoC binary with MoC.cPlugin support..." -ForegroundColor Cyan
& ".\install.ps1"
Write-Host "`nReady! You can now open ETABS and MoC will load smoothly without any errors." -ForegroundColor Green
