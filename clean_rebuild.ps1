$ErrorActionPreference = 'Continue'
$src = 'E:\Atest1.0\AudioStudio\AudioStudio'
$pub = Join-Path $src 'publish'
$proj = Join-Path $src 'AudioStudio.csproj'

Write-Host "=== CLEAN REBUILD ===" 
Write-Host (Get-Date)

# Clean publish folder
if (Test-Path $pub) {
    Write-Host "Removing publish folder..."
    Remove-Item $pub -Recurse -Force
}

# Build
Set-Location $src
Write-Host "Building..."
$build = dotnet build -c Release $proj 2>&1
Write-Host "Build result: $LASTEXITCODE"

# Publish
Write-Host "Publishing..."
$pubResult = dotnet publish -c Release -r win-x64 --self-contained true -o $pub 2>&1
Write-Host "Publish result: $LASTEXITCODE"

# Verify
$exe = Join-Path $pub 'AudioStudio.exe'
if (Test-Path $exe) {
    $ts = (Get-Item $exe).LastWriteTime
    Write-Host "SUCCESS - AudioStudio.exe: $ts"
    
    # Copy timestamp to verify file
    "$ts" | Out-File (Join-Path $src 'publish_time.txt')
} else {
    Write-Host "ERROR: AudioStudio.exe not found"
}
