<#
.SYNOPSIS
    Installs MoCSI (ETABS Structural & BIM Suite) onto the workstation like MoBim.
.DESCRIPTION
    Runs the comprehensive deployment script matching MoBim's exact standards:
    - Revit 2027 and 2026 Add-in deployment (%APPDATA%\Autodesk\Revit\Addins\<Year>\MoCSI.addin)
    - In-Process ETABS 22 plugin integration (Tools > MoCSI - Structural Suite)
    - Standalone Windows Application & Desktop shortcuts
    - Python automation suite with CustomTkinter GUI & SBC 301 database
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
