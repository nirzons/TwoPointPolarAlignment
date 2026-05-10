$packagesDir = "$env:USERPROFILE\.nuget\packages"
$dllPaths = @(
    "nina.core\3.0.0.2017-beta\lib\net8.0-windows7.0\NINA.Core.dll",
    "nina.equipment\3.0.0.2017-beta\lib\net8.0-windows7.0\NINA.Equipment.dll",
    "nina.platesolving\3.0.0.2017-beta\lib\net8.0-windows7.0\NINA.Platesolving.dll",
    "nina.plugin\3.0.0.2017-beta\lib\net8.0-windows7.0\NINA.Plugin.dll",
    "nina.profile\3.0.0.2017-beta\lib\net8.0-windows7.0\NINA.Profile.dll",
    "nina.sequencer\3.0.0.2017-beta\lib\net8.0-windows7.0\NINA.Sequencer.dll",
    "nina.wpf.base\3.0.0.2017-beta\lib\net8.0-windows7.0\NINA.WPF.Base.dll"
)

foreach ($p in $dllPaths) {
    $fullPath = Join-Path $packagesDir $p
    if (Test-Path $fullPath) {
        try {
            $assembly = [System.Reflection.Assembly]::LoadFrom($fullPath)
            try {
                $types = $assembly.GetTypes()
            } catch [System.Reflection.ReflectionTypeLoadException] {
                $types = $_.Exception.Types | Where-Object { $_ -ne $null }
            }
            $matches = $types | Where-Object { $_.Name -match "CaptureSequence" -or $_.Name -match "CameraInfo" -or $_.Name -match "PlateSolve" -or $_.Name -match "Coordinates" }
            if ($matches) {
                Write-Output "=== Found in $($assembly.GetName().Name) ==="
                foreach ($t in $matches) {
                    Write-Output "  $($t.FullName)"
                }
            }
        } catch {
            Write-Output "Failed: $p"
        }
    }
}
