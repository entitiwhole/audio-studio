$ErrorActionPreference = 'Continue'
$log = @()

$log += "=== Starting Publish ==="
$log += "Working dir: $(Get-Location)"
$log += "Time: $(Get-Date)"

# Clean and publish
$publishDir = "E:\Atest1.0\AudioStudio\AudioStudio\publish_clean"
if (Test-Path $publishDir) {
    $log += "Removing old publish folder..."
    Remove-Item $publishDir -Recurse -Force
}

$log += "Running dotnet publish..."
$log += "Command: dotnet publish -c Release -r win-x64 --self-contained true -o $publishDir"

Push-Location "E:\Atest1.0\AudioStudio\AudioStudio"
try {
    $output = dotnet publish -c Release -r win-x64 --self-contained true -o $publishDir 2>&1 | Out-String
    $log += "Exit code: $LASTEXITCODE"
    $log += "Output length: $($output.Length)"
    $log += "Output: $output"
} catch {
    $log += "ERROR: $_"
}
Pop-Location

# Check result
$log += "`n=== Checking Result ==="
if (Test-Path $publishDir) {
    $log += "Publish folder exists"
    $files = Get-ChildItem $publishDir -File -Recurse -ErrorAction SilentlyContinue
    $log += "Files found: $($files.Count)"
    foreach ($f in $files | Select-Object -First 10) {
        $log += "  - $($f.Name) ($($f.Length) bytes)"
    }
    if ($files.Count -gt 10) {
        $log += "  ... and $($files.Count - 10) more files"
    }
    
    # Check key files
    $exePath = Join-Path $publishDir "AudioStudio.exe"
    if (Test-Path $exePath) {
        $log += "EXE found: OK"
    } else {
        $log += "EXE NOT FOUND!"
    }
    
    $dllPath = Join-Path $publishDir "AudioStudio.dll"
    if (Test-Path $dllPath) {
        $log += "DLL found: OK"
    } else {
        $log += "DLL NOT FOUND!"
    }
    
    $coreclrPath = Join-Path $publishDir "coreclr.dll"
    if (Test-Path $coreclrPath) {
        $log += "coreclr.dll found: OK"
    } else {
        $log += "coreclr.dll NOT FOUND!"
    }
} else {
    $log += "Publish folder NOT created!"
}

$log += "`n=== Done ==="

$log | Out-File "E:\Atest1.0\AudioStudio\publish_log.txt" -Encoding UTF8
Write-Host ($log | Out-String)