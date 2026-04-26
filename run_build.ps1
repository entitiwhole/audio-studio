param([string]$Path="E:\Atest1.0\AudioStudio\AudioStudioInstaller")
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$log = @()
$log += "=== Build Log ==="
$log += "Time: $(Get-Date)"
$log += "Path: $Path"

# Check WiX installation
$wixTool = Get-Command candle.exe -ErrorAction SilentlyContinue
$log += "WiX candle: $($wixTool.Source)"

# Check source files
$exePath = "E:\Atest1.0\AudioStudio\AudioStudio\publish\AudioStudio.exe"
if (Test-Path $exePath) {
    $log += "EXE exists: $exePath"
    $log += "EXE size: $((Get-Item $exePath).Length) bytes"
} else {
    $log += "ERROR: EXE not found!"
}

$dllPath = "E:\Atest1.0\AudioStudio\AudioStudio\publish\AudioStudio.dll"
if (Test-Path $dllPath) {
    $log += "DLL exists: $dllPath"
} else {
    $log += "WARNING: DLL not found"
}

# Run build
$log += "`n=== Running Build ==="
Push-Location $Path
try {
    $output = dotnet build 2>&1
    $log += "Build output length: $($output.Length)"
    $log += "Exit code: $LASTEXITCODE"
    
    # Write full output to separate file
    $output | Out-File "E:\Atest1.0\AudioStudio\build_full_output.txt" -Encoding utf8
    
    # Show errors if any
    $errors = $output | Where-Object { $_ -match "error" -or $_ -match "Error" -or $_ -match "ERROR" }
    if ($errors) {
        $log += "Errors found:"
        $errors | ForEach-Object { $log += $_ }
    }
} catch {
    $log += "EXCEPTION: $_"
    $log += $_.Exception.Message
}
Pop-Location

# Write log
$log | Out-File "E:\Atest1.0\AudioStudio\build_log.txt" -Encoding utf8
Write-Host "Done"