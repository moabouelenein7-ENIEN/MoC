<#
.SYNOPSIS
  Deploys MoCSI Structural Suite into Revit and ETABS exactly following MoBim's deployment pattern.
.DESCRIPTION
  1. Checks that Revit and ETABS are closed (prevents file locks and config overwrites).
  2. Builds MoCSI in Release mode for both .NET 10 (Revit) and .NET 8 (ETABS).
  3. Backs up previous installations to %USERPROFILE%\MoCSI-backups\deploy-<timestamp>.
  4. Deploys Revit Add-in to %APPDATA%\Autodesk\Revit\Addins\2027 (and 2026):
     - MoCSI.addin manifest
     - MoCSI\bin\ binaries (.NET 10)
     - MoCSI\install.json record
  5. Deploys ETABS In-Process Plugin & Standalone App to %LOCALAPPDATA%\Programs\MoCSI (.NET 8):
     - Configures ETABS.ini and ETABSv22.settings
  6. Creates Desktop and Start Menu shortcuts.
  7. Registers in Windows Installed Apps (HKCU).
#>
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$DryRun,
    [switch]$ForceKill
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'src\EtabsHelper\EtabsHelper.csproj'
$manifestSrc = Join-Path $root 'MoCSI.addin'

function Say([string]$text, [string]$colour = 'Gray') { Write-Host $text -ForegroundColor $colour }

Say "==========================================================" Cyan
Say "   MoCSI - Structural & BIM Suite Deployment (MoBim)      " Yellow
Say "==========================================================" Cyan

# ---- 1. Check Revit & ETABS processes
if ($ForceKill) {
    Get-Process -Name Revit, ETABS -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$revit = Get-Process -Name Revit -ErrorAction SilentlyContinue
if ($revit) {
    Say "Revit is currently running (PID $($revit.Id -join ', ')). Close Revit and run again. Nothing was copied." Red
    exit 1
}

$etabs = Get-Process -Name ETABS -ErrorAction SilentlyContinue
if ($etabs) {
    Say "ETABS is currently running (PID $($etabs.Id -join ', ')). Close ETABS and run again (ETABS will overwrite settings if open). Nothing was copied." Red
    exit 1
}

# ---- 2. Build multi-target Release
Say "Building MoCSI ($Configuration)..." Cyan
dotnet build $csproj -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) {
    Say "Build failed - nothing was copied." Red
    exit 1
}

$stage10 = Join-Path $root "src\EtabsHelper\bin\$Configuration\net10.0-windows"
$stage8  = Join-Path $root "src\EtabsHelper\bin\$Configuration\net8.0-windows"

if (-not (Test-Path (Join-Path $stage10 'MoCSI.dll'))) {
    Say "net10.0-windows build missing MoCSI.dll - nothing was copied." Red
    exit 1
}
if (-not (Test-Path (Join-Path $stage8 'MoCSI.dll'))) {
    Say "net8.0-windows build missing MoCSI.dll - nothing was copied." Red
    exit 1
}

# ---- 3. Backup previous installations
$timestamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
$backupRoot = Join-Path $env:USERPROFILE ("MoCSI-backups\deploy-" + $timestamp)
New-Item -ItemType Directory -Force $backupRoot | Out-Null
Say "Backup directory: $backupRoot" Gray

# ---- 4. Deploy to Revit 2027 and 2026
$revitYears = @('2027', '2026')
foreach ($year in $revitYears) {
    $addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    if (Test-Path $addinsDir) {
        Say "Deploying to Revit $year add-ins ($addinsDir)..." Cyan
        $targetDir = Join-Path $addinsDir 'MoCSI'
        $binDir = Join-Path $targetDir 'bin'
        $recFile = Join-Path $targetDir 'install.json'
        $targetManifest = Join-Path $addinsDir 'MoCSI.addin'

        # Remove legacy MoC manifest and folder if present
        $oldManifest = Join-Path $addinsDir 'MoC.addin'
        $oldTarget = Join-Path $addinsDir 'MoC'
        if (Test-Path $oldManifest) { Remove-Item -Force $oldManifest }
        if (Test-Path $oldTarget) { Remove-Item -Recurse -Force $oldTarget }

        # Backup old MoCSI if exists
        if (Test-Path $binDir) {
            $yearBackup = Join-Path $backupRoot "Revit$year"
            New-Item -ItemType Directory -Force $yearBackup | Out-Null
            Get-ChildItem $binDir -File | Copy-Item -Destination $yearBackup -Force
        }

        if (-not $DryRun) {
            New-Item -ItemType Directory -Force $binDir | Out-Null

            # Copy manifest
            Copy-Item $manifestSrc -Destination $targetManifest -Force

            # Copy net10 binaries
            $revitFiles = Get-ChildItem $stage10 -File | Where-Object { $_.Name -match '^MoCSI\.(dll|pdb|deps\.json|runtimeconfig\.json)$' }
            $deployedList = @('MoCSI.addin')
            foreach ($f in $revitFiles) {
                $dest = Join-Path $binDir $f.Name
                Copy-Item $f.FullName -Destination $dest -Force
                $deployedList += "MoCSI\bin\" + $f.Name
            }

            # Write install.json
            $rec = [ordered]@{
                Product = "MoCSI - ETABS Structural & BIM Suite"
                Target = "Revit $year"
                Version = "1.0.0"
                Installed = (Get-Date -Format 'yyyy-MM-dd HH:mm')
                Files = $deployedList
            }
            [IO.File]::WriteAllText($recFile, ($rec | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
            Say "   Deployed MoCSI to Revit $year successfully!" Green
        }
    }
}

# ---- 5. Deploy to ETABS 22 & Local Programs
$etabsInstallDir = Join-Path $env:LOCALAPPDATA "Programs\MoCSI"
Say "Deploying to ETABS Suite ($etabsInstallDir)..." Cyan

if (-not $DryRun) {
    New-Item -ItemType Directory -Force $etabsInstallDir | Out-Null

    # Copy net8 binaries for ETABS
    $etabsFiles = Get-ChildItem $stage8 -File | Where-Object { $_.Name -match '^MoCSI\.(dll|exe|pdb|deps\.json|runtimeconfig\.json)$' }
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

# ---- 6. Shortcuts & Windows Registry
$WshShell = New-Object -ComObject WScript.Shell
$exePath = Join-Path $etabsInstallDir "MoCSI.exe"

# Clean old shortcuts
$desktopDir = [Environment]::GetFolderPath("Desktop")
$oldLnk = Join-Path $desktopDir "MoC.lnk"
if (Test-Path $oldLnk) { Remove-Item -Force $oldLnk }
$programsDir = [Environment]::GetFolderPath("Programs")
$oldProgLnk = Join-Path $programsDir "MoC.lnk"
if (Test-Path $oldProgLnk) { Remove-Item -Force $oldProgLnk }

$desktopLnk = Join-Path $desktopDir "MoCSI.lnk"
$s1 = $WshShell.CreateShortcut($desktopLnk)
$s1.TargetPath = $exePath
$s1.WorkingDirectory = $etabsInstallDir
$s1.Description = "MoCSI - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s1.Save()

$startMenuLnk = Join-Path $programsDir "MoCSI.lnk"
$s2 = $WshShell.CreateShortcut($startMenuLnk)
$s2.TargetPath = $exePath
$s2.WorkingDirectory = $etabsInstallDir
$s2.Description = "MoCSI - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s2.Save()

# Registry update
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MoC" -Recurse -Force -ErrorAction SilentlyContinue
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MoCSI"
if (-not (Test-Path $uninstallKey)) {
    New-Item -Path $uninstallKey -Force | Out-Null
}
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "MoCSI - ETABS & Revit Structural Suite"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "MoCSI Structural"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $etabsInstallDir
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$exePath,0"

Say "`nDeployment Complete!" Green
Say "   Revit 2027 Tab:  'MoCSI' -> ETABS Suite, SBC 301 Seismic, Wall Loads, Rebar Detailer" Cyan
Say "   ETABS 22 Menu:   'Tools' -> 'MoCSI - Structural Suite'" Cyan
Say "   Standalone App:   $exePath" Cyan
Say "   Desktop Shortcut: $desktopLnk" Cyan
