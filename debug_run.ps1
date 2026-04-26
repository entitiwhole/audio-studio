param(
    [string]$ExePath = "C:\Program Files\AudioStudio Demo\AudioStudio.exe",
    [string]$LogPath = "$env:TEMP\AudioStudioDebug.txt"
)

$log = @()
$log += "=== AudioStudio Debug ==="
$log += "Time: $(Get-Date)"
$log += "ExePath: $ExePath"

# Check if installed
if (Test-Path $ExePath) {
    $log += "EXE found at $ExePath"
    $files = Get-ChildItem "C:\Program Files\AudioStudio Demo" -File -ErrorAction SilentlyContinue
    $log += "Files in folder: $($files.Count)"
    $files | ForEach-Object { $log += "  - $($_.Name)" }
} else {
    $log += "EXE not found at $ExePath"
    # Try to find where it was installed
    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*"
    $installed = Get-ItemProperty $regPath -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like "*Audio*" }
    if ($installed) {
        $log += "Found in registry: $($installed.InstallLocation)"
    }
}

# Check .NET version
$log += "`n=== .NET Versions ==="
$dotnetVersions = Get-ChildItem "HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions" -ErrorAction SilentlyContinue
$dotnetVersions | ForEach-Object {
    $log += "Version: $($_.Name)"
}

# Try to run the app and capture error
$log += "`n=== Try Run ==="
try {
    $errLog = "$env:TEMP\app_err.log"
    $proc = Start-Process $ExePath -PassThru -RedirectStandardError $errLog -WindowStyle Hidden
    Start-Sleep 3
    if ($proc.HasExited) {
        $log += "App exited with code: $($proc.ExitCode)"
        if (Test-Path $errLog) {
            $log += "StdErr: $(Get-Content $errLog -Raw)"
        }
    } else {
        $log += "App is running (PID: $($proc.Id))"
        Stop-Process $proc.Id -Force -ErrorAction SilentlyContinue
    }
} catch {
    $log += "ERROR: $_"
}

# Windows Event Log
$log += "`n=== Recent Errors ==="
try {
    $errors = Get-WinEvent -FilterHashtable @{LogName='Application';Level=2} -MaxEvents 5 -ErrorAction SilentlyContinue | 
        Where-Object { $_.TimeCreated -gt (Get-Date).AddMinutes(-5) }
    if ($errors) {
        $errors | ForEach-Object { 
            $log += "Time: $($_.TimeCreated)"
            $log += "Message: $($_.Message)"
            $log += "---"
        }
    } else {
        $log += "No recent errors"
    }
} catch {
    $log += "Could not read event log: $_"
}

$log | Out-File $LogPath -Encoding UTF8
Write-Host "Log saved to $LogPath"
Write-Host ($log | Out-String)