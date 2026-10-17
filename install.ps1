<#
.SYNOPSIS
    Installs MoCSI (ETABS Structural Suite) onto the workstation.
.DESCRIPTION
    Runs the deployment script strictly for CSI ETABS and Standalone Suite:
    - In-Process ETABS 22 plugin integration (Tools > MoCSI - Structural Suite)
    - Standalone Windows Application (%LOCALAPPDATA%\Programs\MoCSI\MoCSI.exe) & Desktop shortcuts
    - Python automation suite with CustomTkinter GUI & SBC 301 database
    (MoBim remains strictly your Revit add-in; MoCSI does not touch Revit).
#>
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$ForceKill
)

$deployScript = Join-Path $PSScriptRoot "tools\deploy.ps1"
if ($ForceKill) {
    & $deployScript -Configuration $Configuration -ForceKill
} else {
    & $deployScript -Configuration $Configuration
}
