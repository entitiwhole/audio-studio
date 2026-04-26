$ErrorActionPreference = 'Continue'
$src = 'E:\Atest1.0\AudioStudio\AudioStudio'
$pub = Join-Path $src 'publish'

Write-Host "Starting publish..."

Set-Location $src; dotnet publish -c Release -r win-x64 --self-contained true -o $pub 2>&1 | Tee-Object -Variable output

Write-Host "Exit code: $LASTEXITCODE"

# Check result
$exe = Join-Path $pub 'AudioStudio.exe'
$time = (Get-Item $exe).LastWriteTime
Write-Host "AudioStudio.exe updated: $time"

$log = "$env:USERPROFILE\Desktop\publish_result.txt"
"$output`n`nUpdated: $time" | Out-File $log -Encoding UTF8
Write-Host "Log saved to $log"
