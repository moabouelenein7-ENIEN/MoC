<#
.SYNOPSIS
  Deploys MoCSI (CSI ETABS Structural Modeling & Design Suite).
.DESCRIPTION
  MoCSI is exclusively an ETABS tool and standalone automation suite (SBC 301 / SBC 304).
  (Note: MoBim is strictly the Revit add-in; MoCSI does NOT touch or install into Revit).

  1. Checks that ETABS is closed before deployment (prevents file locks and config overwrites).
  2. Builds MoCSI in Release mode (.NET 8 for ETABS 22 native compatibility).
  3. Backs up previous installations to %USERPROFILE%\MoCSI-backups\deploy-<timestamp>.
  4. Deploys ETABS In-Process Plugin & Standalone App to %LOCALAPPDATA%\Programs\MoCSI:
     - Deploys MoCSI.dll, MoCSI.exe, dependencies, and CSiAPIv1.dll
     - Configures ETABS.ini and ETABSv22.settings (Tools > MoCSI - Structural Suite)
  5. Creates Desktop and Start Menu shortcuts.
  6. Registers MoCSI in Windows Installed Apps (HKCU).
#>
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$DryRun,
    [switch]$ForceKill
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'src\EtabsHelper\EtabsHelper.csproj'

function Say([string]$text, [string]$colour = 'Gray') { Write-Host $text -ForegroundColor $colour }

Say "==========================================================" Cyan
Say "   MoCSI - ETABS Structural Suite Deployment              " Yellow
Say "   (Dedicated strictly to CSI ETABS & Standalone Suite)    " Gray
Say "==========================================================" Cyan

# ---- 1. Check ETABS process
if ($ForceKill) {
    Get-Process -Name ETABS -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$etabs = Get-Process -Name ETABS -ErrorAction SilentlyContinue
if ($etabs) {
    Say "ETABS is currently running (PID $($etabs.Id -join ', ')). Close ETABS and run again (ETABS will overwrite settings if open). Nothing was copied." Red
    exit 1
}

# ---- 2. Build Release (.NET 8 for ETABS)
Say "Building MoCSI ($Configuration)..." Cyan
dotnet build $csproj -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) {
    Say "Build failed - nothing was copied." Red
    exit 1
}

$stage = Join-Path $root "src\EtabsHelper\bin\$Configuration\net8.0-windows"
if (-not (Test-Path (Join-Path $stage 'MoCSI.dll'))) {
    Say "Build missing MoCSI.dll - nothing was copied." Red
    exit 1
}

# ---- 3. Backup previous installation
$timestamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
$backupRoot = Join-Path $env:USERPROFILE ("MoCSI-backups\deploy-" + $timestamp)
New-Item -ItemType Directory -Force $backupRoot | Out-Null
Say "Backup directory: $backupRoot" Gray

# ---- 4. Deploy to ETABS 22 & Local Programs
$etabsInstallDir = Join-Path $env:LOCALAPPDATA "Programs\MoCSI"
Say "Deploying to ETABS Suite ($etabsInstallDir)..." Cyan

if (-not $DryRun) {
    # Backup old binaries if present
    if (Test-Path $etabsInstallDir) {
        Get-ChildItem $etabsInstallDir -File | Copy-Item -Destination $backupRoot -Force
    }

    New-Item -ItemType Directory -Force $etabsInstallDir | Out-Null

    # Copy .NET 8 binaries
    $etabsFiles = Get-ChildItem $stage -File | Where-Object { $_.Name -match '^MoCSI\.(dll|exe|pdb|deps\.json|runtimeconfig\.json)$' }
    foreach ($f in $etabsFiles) {
        Copy-Item $f.FullName -Destination (Join-Path $etabsInstallDir $f.Name) -Force
    }

    # Locate and copy CSiAPIv1.dll
    $csiDll = "C:\Program Files\Computers and Structures\ETABS 22\CSiAPIv1.dll"
    if (Test-Path $csiDll) {
        Copy-Item $csiDll -Destination (Join-Path $etabsInstallDir "CSiAPIv1.dll") -Force
    }

    # Register in ETABSv22.settings and ETABS.ini
    $csiAppDir = Join-Path $env:LOCALAPPDATA "Computers and Structures\ETABS 22"
    if (Test-Path $csiAppDir) {
        $etabsDllPath = Join-Path $etabsInstallDir "MoCSI.dll"

        # Settings XML
        $settingsPath = Join-Path $csiAppDir "ETABSv22.settings"
        if (Test-Path $settingsPath) {
            $xml = [xml](Get-Content $settingsPath)
            function Set-Node($parent, $name, $val) {
                $node = $parent.SelectSingleNode($name)
                if ($null -eq $node) {
                    $node = $xml.CreateElement($name)
                    [void]$parent.AppendChild($node)
                }
                $node.InnerText = $val
            }
            Set-Node $xml.Settings "Plugin1Name" "MoCSI"
            Set-Node $xml.Settings "Plugin1MenuText" "MoCSI - Structural Suite"
            Set-Node $xml.Settings "Plugin1Path" $etabsDllPath
            Set-Node $xml.Settings "PlugIn" "<?xml version=""1.0"" encoding=""utf-16""?>`r`n<ArrayOfString xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">`r`n  <string>MoCSI</string>`r`n</ArrayOfString>"
            $xml.Save($settingsPath)
            Say "   Configured ETABSv22.settings" Green
        }

        # ETABS.ini
        $iniPath = Join-Path $csiAppDir "ETABS.ini"
        if (Test-Path $iniPath) {
            $ini = Get-Content $iniPath -Raw
            $plugInBlock = "[PlugIn]`r`n NumberPlugIns=1`r`n PlugInName1=MoCSI`r`n PlugInFileName1=$etabsDllPath`r`n PlugInMenuText1=MoCSI - Structural Suite`r`n PlugInType1=2"
            if ($ini -match "\[PlugIn\][\s\S]*?(?=\r?\n\[|\Z)") {
                $ini = $ini -replace "\[PlugIn\][\s\S]*?(?=\r?\n\[|\Z)", $plugInBlock
            } else {
                $ini += "`r`n`r`n" + $plugInBlock
            }
            Set-Content -Path $iniPath -Value $ini -Encoding Ascii
            Say "   Configured ETABS.ini" Green
        }
    }
}

# ---- 5. Shortcuts & Windows Registry
$WshShell = New-Object -ComObject WScript.Shell
$exePath = Join-Path $etabsInstallDir "MoCSI.exe"

$desktopDir = [Environment]::GetFolderPath("Desktop")
$desktopLnk = Join-Path $desktopDir "MoCSI.lnk"
$s1 = $WshShell.CreateShortcut($desktopLnk)
$s1.TargetPath = $exePath
$s1.WorkingDirectory = $etabsInstallDir
$s1.Description = "MoCSI - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s1.Save()

$programsDir = [Environment]::GetFolderPath("Programs")
$startMenuLnk = Join-Path $programsDir "MoCSI.lnk"
$s2 = $WshShell.CreateShortcut($startMenuLnk)
$s2.TargetPath = $exePath
$s2.WorkingDirectory = $etabsInstallDir
$s2.Description = "MoCSI - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s2.Save()

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MoCSI"
if (-not (Test-Path $uninstallKey)) {
    New-Item -Path $uninstallKey -Force | Out-Null
}
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "MoCSI - ETABS Structural Modeling & Design Suite"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "MoCSI Structural"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $etabsInstallDir
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$exePath,0"

Say "`nDeployment Complete!" Green
Say "   ETABS 22 Menu:    'Tools' -> 'MoCSI - Structural Suite'" Cyan
Say "   Standalone App:    $exePath" Cyan
Say "   Desktop Shortcut:  $desktopLnk" Cyan
Say "   Python Automation: Run_MoCSI_Python.bat" Cyan
