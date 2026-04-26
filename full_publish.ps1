$ErrorActionPreference = 'Continue'
$pub = 'E:\Atest1.0\AudioStudio\AudioStudio\publish'
$proj = 'E:\Atest1.0\AudioStudio\AudioStudio\AudioStudio.csproj'

Write-Host "=== Starting Publish ==="
Write-Host (Get-Date)

Set-Location (Split-Path $proj)

# Clean and publish
if (Test-Path $pub) {
    Remove-Item $pub -Recurse -Force
}

Write-Host "Publishing..."
$result = dotnet publish -c Release -r win-x64 --self-contained true -o $pub 2>&1

Write-Host "Result:"
$result

# Verify
$exe = Join-Path $pub 'AudioStudio.exe'
if (Test-Path $exe) {
    $ts = (Get-Item $exe).LastWriteTime
    Write-Host "SUCCESS: AudioStudio.exe timestamp: $ts"
} else {
    Write-Host "ERROR: AudioStudio.exe not found"
}
