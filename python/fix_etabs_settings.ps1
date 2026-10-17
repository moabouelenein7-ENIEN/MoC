$csiAppDir = "$env:LOCALAPPDATA\Computers and Structures\ETABS 22"
$dllPath = "C:\Users\imrua\AppData\Local\Programs\MoC\MoC.dll"

# 1. Update ETABS.ini
$iniPath = Join-Path $csiAppDir "ETABS.ini"
if (Test-Path $iniPath) {
    $ini = Get-Content $iniPath -Raw
    $plugInBlock = "[PlugIn]`r`n NumberPlugIns=1`r`n PlugInName1=MoC`r`n PlugInFileName1=$dllPath`r`n PlugInMenuText1=MoC - Structural Suite`r`n PlugInType1=2"
    if ($ini -match "\[PlugIn\][\s\S]*?(?=\r?\n\[|\Z)") {
        $ini = $ini -replace "\[PlugIn\][\s\S]*?(?=\r?\n\[|\Z)", $plugInBlock
    } else {
        $ini += "`r`n`r`n" + $plugInBlock
    }
    Set-Content -Path $iniPath -Value $ini -Encoding Ascii
    Write-Host "Updated ETABS.ini successfully."
}

# 2. Update ETABSv22.settings
$settingsPath = Join-Path $csiAppDir "ETABSv22.settings"
if (Test-Path $settingsPath) {
    $xml = [xml](Get-Content $settingsPath)
    
    function Set-NodeText($parent, $name, $val) {
        $node = $parent.SelectSingleNode($name)
        if ($null -eq $node) {
            $node = $xml.CreateElement($name)
            [void]$parent.AppendChild($node)
        }
        $node.InnerText = $val
    }

    Set-NodeText $xml.Settings "Plugin1Name" "MoC"
    Set-NodeText $xml.Settings "Plugin1MenuText" "MoC - Structural Suite"
    Set-NodeText $xml.Settings "Plugin1Path" $dllPath
    Set-NodeText $xml.Settings "Plugin1Info" ""
    
    # Crucial: The PlugIn XML string array
    $pluginArrayXml = '<?xml version="1.0" encoding="utf-16"?>' + "`r`n" + '<ArrayOfString xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">' + "`r`n" + '  <string>MoC</string>' + "`r`n" + '</ArrayOfString>'
    Set-NodeText $xml.Settings "PlugIn" $pluginArrayXml

    $xml.Save($settingsPath)
    Write-Host "Updated ETABSv22.settings successfully."
}
