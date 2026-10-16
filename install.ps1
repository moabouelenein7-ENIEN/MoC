<#
.SYNOPSIS
    Installs MoC (ETABS Structural Modeling & Design Suite) onto the workstation.
.DESCRIPTION
    1. Publishes Release binaries to %LOCALAPPDATA%\Programs\MoC
    2. Resolves and copies CSiAPIv1.dll from installed ETABS
    3. Creates Desktop shortcut
    4. Creates Start Menu shortcut
    5. Registers MoC in Windows Apps & Features (Settings > Apps)
#>

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   MoC - ETABS Structural Modeling & Design Suite Setup   " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$scriptRoot = $PSScriptRoot
$projectFile = Join-Path $scriptRoot "src\EtabsHelper\EtabsHelper.csproj"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\MoC"

# 1. Locate ETABS Installation & CSiAPIv1.dll
Write-Host "[1/5] Detecting ETABS installation..." -ForegroundColor Gray
$etabsRoots = @(
    "$env:ProgramFiles\Computers and Structures",
    "${env:ProgramFiles(x86)}\Computers and Structures"
)

$csiDll = $null
foreach ($root in $etabsRoots) {
    if (Test-Path $root) {
        $found = Get-ChildItem -Path $root -Filter "CSiAPIv1.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $csiDll = $found.FullName
            break
        }
    }
}

if (-not $csiDll) {
    Write-Warning "CSiAPIv1.dll not found in standard ETABS locations. Looking in ETABS 22 default..."
    $fallback = "C:\Program Files\Computers and Structures\ETABS 22\CSiAPIv1.dll"
    if (Test-Path $fallback) { $csiDll = $fallback }
}

if ($csiDll) {
    Write-Host "      Found CSiAPIv1.dll at: $csiDll" -ForegroundColor Green
} else {
    Write-Warning "      ETABS not detected. Proceeding with standard build."
}

# 2. Publish Binaries
Write-Host "[2/5] Publishing MoC binaries to $installDir..." -ForegroundColor Gray
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

& dotnet publish $projectFile -c Release -o $installDir -r win-x64 --self-contained false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if ($csiDll -and (Test-Path $csiDll)) {
    Copy-Item $csiDll -Destination (Join-Path $installDir "CSiAPIv1.dll") -Force
}

$exePath = Join-Path $installDir "MoC.exe"
$dllPath = Join-Path $installDir "MoC.dll"

# 3. Create Desktop and Start Menu Shortcuts
Write-Host "[3/5] Creating shortcuts..." -ForegroundColor Gray
$WshShell = New-Object -ComObject WScript.Shell

$desktopDir = [Environment]::GetFolderPath("Desktop")
$desktopLnk = Join-Path $desktopDir "MoC.lnk"
$s1 = $WshShell.CreateShortcut($desktopLnk)
$s1.TargetPath = $exePath
$s1.WorkingDirectory = $installDir
$s1.Description = "MoC - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s1.Save()
Write-Host "      Desktop Shortcut: $desktopLnk" -ForegroundColor Green

$startMenuDir = [Environment]::GetFolderPath("Programs")
$startMenuLnk = Join-Path $startMenuDir "MoC.lnk"
$s2 = $WshShell.CreateShortcut($startMenuLnk)
$s2.TargetPath = $exePath
$s2.WorkingDirectory = $installDir
$s2.Description = "MoC - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s2.Save()
Write-Host "      Start Menu Shortcut: $startMenuLnk" -ForegroundColor Green

# 4. Generate Uninstaller Script
Write-Host "[4/5] Writing uninstall script..." -ForegroundColor Gray
$uninstScript = Join-Path $installDir "uninstall.ps1"
@"
`$installDir = '$installDir'
`$desktopLnk = '$desktopLnk'
`$startMenuLnk = '$startMenuLnk'
Write-Host 'Uninstalling MoC...' -ForegroundColor Yellow
if (Test-Path `$desktopLnk) { Remove-Item -Force `$desktopLnk }
if (Test-Path `$startMenuLnk) { Remove-Item -Force `$startMenuLnk }
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MoC' -Recurse -Force -ErrorAction SilentlyContinue
Start-Process cmd.exe -ArgumentList '/c timeout /t 2 & rmdir /s /q \"'`$installDir'\"' -WindowStyle Hidden
Write-Host 'MoC successfully uninstalled.' -ForegroundColor Green
"@ | Out-File -FilePath $uninstScript -Encoding utf8

# 5. Register in Windows Settings > Apps
Write-Host "[5/5] Registering in Windows Installed Apps..." -ForegroundColor Gray
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MoC"
if (-not (Test-Path $uninstallKey)) {
    New-Item -Path $uninstallKey -Force | Out-Null
}
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "MoC - ETABS Structural Modeling & Design Suite"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "MoC Structural"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $installDir
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$exePath,0"
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$uninstScript`""

Write-Host "`nInstallation Completed Successfully!" -ForegroundColor Green
Write-Host "Application Path: $exePath" -ForegroundColor White
Write-Host "ETABS Plugin DLL: $dllPath" -ForegroundColor White
Write-Host "You can now launch MoC directly from your Desktop or from within ETABS (Tools > Add/Show Plug Ins)!" -ForegroundColor Cyan
