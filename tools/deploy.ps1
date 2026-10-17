<#
.SYNOPSIS
  Deploys MoC Structural Suite into Revit and ETABS exactly following MoBim's deployment pattern.
.DESCRIPTION
  1. Checks that Revit and ETABS are closed (prevents file locks and config overwrites).
  2. Builds MoC in Release mode for both .NET 10 (Revit) and .NET 8 (ETABS).
  3. Backs up previous installations to %USERPROFILE%\MoC-backups\deploy-<timestamp>.
  4. Deploys Revit Add-in to %APPDATA%\Autodesk\Revit\Addins\2027 (and 2026):
     - MoC.addin manifest
     - MoC\bin\ binaries (.NET 10)
     - MoC\install.json record
  5. Deploys ETABS In-Process Plugin & Standalone App to %LOCALAPPDATA%\Programs\MoC (.NET 8):
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
$manifestSrc = Join-Path $root 'MoC.addin'

function Say([string]$text, [string]$colour = 'Gray') { Write-Host $text -ForegroundColor $colour }

Say "==========================================================" Cyan
Say "   MoC - Structural & BIM Suite Deployment (MoBim Pattern) " Yellow
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
Say "Building MoC ($Configuration)..." Cyan
dotnet build $csproj -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) {
    Say "Build failed - nothing was copied." Red
    exit 1
}

$stage10 = Join-Path $root "src\EtabsHelper\bin\$Configuration\net10.0-windows"
$stage8  = Join-Path $root "src\EtabsHelper\bin\$Configuration\net8.0-windows"

if (-not (Test-Path (Join-Path $stage10 'MoC.dll'))) {
    Say "net10.0-windows build missing MoC.dll - nothing was copied." Red
    exit 1
}
if (-not (Test-Path (Join-Path $stage8 'MoC.dll'))) {
    Say "net8.0-windows build missing MoC.dll - nothing was copied." Red
    exit 1
}

# ---- 3. Backup previous installations
$timestamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
$backupRoot = Join-Path $env:USERPROFILE ("MoC-backups\deploy-" + $timestamp)
New-Item -ItemType Directory -Force $backupRoot | Out-Null
Say "Backup directory: $backupRoot" Gray

# ---- 4. Deploy to Revit 2027 and 2026
$revitYears = @('2027', '2026')
foreach ($year in $revitYears) {
    $addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    if (Test-Path $addinsDir) {
        Say "Deploying to Revit $year add-ins ($addinsDir)..." Cyan
        $targetDir = Join-Path $addinsDir 'MoC'
        $binDir = Join-Path $targetDir 'bin'
        $recFile = Join-Path $targetDir 'install.json'
        $targetManifest = Join-Path $addinsDir 'MoC.addin'

        # Backup old
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
            $revitFiles = Get-ChildItem $stage10 -File | Where-Object { $_.Name -match '^MoC\.(dll|pdb|deps\.json|runtimeconfig\.json)$' }
            $deployedList = @('MoC.addin')
            foreach ($f in $revitFiles) {
                $dest = Join-Path $binDir $f.Name
                Copy-Item $f.FullName -Destination $dest -Force
                $deployedList += "MoC\bin\" + $f.Name
            }

            # Write install.json
            $rec = [ordered]@{
                Product = "MoC - ETABS Structural & BIM Suite"
                Target = "Revit $year"
                Version = "1.0.0"
                Installed = (Get-Date -Format 'yyyy-MM-dd HH:mm')
                Files = $deployedList
            }
            [IO.File]::WriteAllText($recFile, ($rec | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
            Say "   Deployed MoC to Revit $year successfully!" Green
        }
    }
}

# ---- 5. Deploy to ETABS 22 & Local Programs
$etabsInstallDir = Join-Path $env:LOCALAPPDATA "Programs\MoC"
Say "Deploying to ETABS Suite ($etabsInstallDir)..." Cyan

if (-not $DryRun) {
    New-Item -ItemType Directory -Force $etabsInstallDir | Out-Null

    # Copy net8 binaries for ETABS
    $etabsFiles = Get-ChildItem $stage8 -File | Where-Object { $_.Name -match '^MoC\.(dll|exe|pdb|deps\.json|runtimeconfig\.json)$' }
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
        $etabsDllPath = Join-Path $etabsInstallDir "MoC.dll"

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
            Set-Node $xml.Settings "Plugin1Name" "MoC"
            Set-Node $xml.Settings "Plugin1MenuText" "MoC - Structural Suite"
            Set-Node $xml.Settings "Plugin1Path" $etabsDllPath
            Set-Node $xml.Settings "PlugIn" "<?xml version=""1.0"" encoding=""utf-16""?>`r`n<ArrayOfString xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">`r`n  <string>MoC</string>`r`n</ArrayOfString>"
            $xml.Save($settingsPath)
            Say "   Configured ETABSv22.settings" Green
        }

        # ETABS.ini
        $iniPath = Join-Path $csiAppDir "ETABS.ini"
        if (Test-Path $iniPath) {
            $ini = Get-Content $iniPath -Raw
            $plugInBlock = "[PlugIn]`r`n NumberPlugIns=1`r`n PlugInName1=MoC`r`n PlugInFileName1=$etabsDllPath`r`n PlugInMenuText1=MoC - Structural Suite`r`n PlugInType1=2"
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
$exePath = Join-Path $etabsInstallDir "MoC.exe"

$desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "MoC.lnk"
$s1 = $WshShell.CreateShortcut($desktopLnk)
$s1.TargetPath = $exePath
$s1.WorkingDirectory = $etabsInstallDir
$s1.Description = "MoC - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s1.Save()

$startMenuLnk = Join-Path ([Environment]::GetFolderPath("Programs")) "MoC.lnk"
$s2 = $WshShell.CreateShortcut($startMenuLnk)
$s2.TargetPath = $exePath
$s2.WorkingDirectory = $etabsInstallDir
$s2.Description = "MoC - ETABS Structural Modeling & Design Suite (SBC 301/304)"
$s2.Save()

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MoC"
if (-not (Test-Path $uninstallKey)) {
    New-Item -Path $uninstallKey -Force | Out-Null
}
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "MoC - ETABS & Revit Structural Suite"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "MoC Structural"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $etabsInstallDir
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$exePath,0"

Say "`nDeployment Complete!" Green
Say "   Revit 2027 Tab:  'MoC' -> ETABS Suite, SBC 301 Seismic, Wall Loads, Rebar Detailer" Cyan
Say "   ETABS 22 Menu:   'Tools' -> 'MoC - Structural Suite'" Cyan
Say "   Standalone App:   $exePath" Cyan
Say "   Desktop Shortcut: $desktopLnk" Cyan
