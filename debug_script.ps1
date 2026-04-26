$ErrorActionPreference = 'Continue'
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$logFile = "E:\Atest1.0\AudioStudio\build_log.txt"

$ErrorActionPreference = 'Continue'
try {
    $result = dotnet nuget list source 2>&1
    "NuGet sources:" | Out-File -FilePath $logFile -Encoding utf8
    $result | Out-File -FilePath $logFile -Append -Encoding utf8
    "---" | Out-File -FilePath $logFile -Append -Encoding utf8
    
    $result = dotnet tool list -g 2>&1
    "Global tools:" | Out-File -FilePath $logFile -Append -Encoding utf8
    $result | Out-File -FilePath $logFile -Append -Encoding utf8
    "---" | Out-File -FilePath $logFile -Append -Encoding utf8
    
    Push-Location "E:\Atest1.0\AudioStudio\AudioStudioInstaller"
    $result = dotnet build 2>&1
    "Build output:" | Out-File -FilePath $logFile -Append -Encoding utf8
    $result | Out-File -FilePath $logFile -Append -Encoding utf8
    Pop-Location
} catch {
    "Error: $_" | Out-File -FilePath $logFile -Append -Encoding utf8
}
"Done" | Out-File -FilePath $logFile -Append -Encoding utf8