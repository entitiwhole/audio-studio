$ErrorActionPreference = 'Continue'
$log = @()

function Add-Log($msg) {
    $log += $msg
}

Add-Log "=== Build Test ==="
Add-Log "Working dir: $(Get-Location)"

# Check file sizes
$pubFiles = Get-ChildItem "E:\Atest1.0\AudioStudio\AudioStudio\publish" -File -ErrorAction SilentlyContinue
$totalSize = ($pubFiles | Measure-Object -Property Length -Sum).Sum
Add-Log "Publish folder: $($pubFiles.Count) files, total: $([math]::Round($totalSize/1KB)) KB"

$msiPath = "E:\Atest1.0\AudioStudio\AudioStudioInstaller\AudioStudioDemo.msi"
$msi = Get-Item $msiPath -ErrorAction SilentlyContinue
if ($msi) {
    Add-Log "MSI size: $([math]::Round($msi.Length/1KB)) KB"
} else {
    Add-Log "MSI not found!"
}

# Build
Push-Location "E:\Atest1.0\AudioStudio\AudioStudioInstaller"
Add-Log "Building..."
try {
    $result = & dotnet build 2>&1
    Add-Log "Build exit code: $LASTEXITCODE"
    if ($result) { Add-Log "Build output: $($result | Out-String)" }
} catch {
    Add-Log "Build exception: $_"
}
Pop-Location

$log | Out-File "E:\Atest1.0\AudioStudio\build_test.txt" -Encoding utf8