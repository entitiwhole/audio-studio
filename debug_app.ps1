# Debug script for AudioStudio MSI
$logFile = "E:\Atest1.0\AudioStudio\debug.log"

"=== Debug Script Started ===" | Out-File $logFile -Encoding UTF8

# Check if publish folder exists
$pubPath = "E:\Atest1.0\AudioStudio\AudioStudio\publish\AudioStudio.exe"
if (Test-Path $pubPath) {
    "Publish exe exists: $pubPath" | Out-File $logFile -Append -Encoding UTF8
    $fileInfo = Get-Item $pubPath
    "Size: $($fileInfo.Length) bytes" | Out-File $logFile -Append -Encoding UTF8
    "LastWriteTime: $($fileInfo.LastWriteTime)" | Out-File $logFile -Append -Encoding UTF8
} else {
    "ERROR: Publish exe does NOT exist!" | Out-File $logFile -Append -Encoding UTF8
}

# Check MSI file
$msiPath = "E:\Atest1.0\AudioStudio\AudioStudioInstaller\AudioStudioDemo.msi"
if (Test-Path $msiPath) {
    "MSI exists: $msiPath" | Out-File $logFile -Append -Encoding UTF8
    $fileInfo = Get-Item $msiPath
    "MSI Size: $($fileInfo.Length) bytes" | Out-File $logFile -Append -Encoding UTF8
} else {
    "ERROR: MSI does NOT exist!" | Out-File $logFile -Append -Encoding UTF8
}

# List installed programs related to AudioStudio
"=== Installed Apps ===" | Out-File $logFile -Append -Encoding UTF8
$installed = Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* 2>$null | Where-Object { $_.DisplayName -like "*Audio*" }
$installed | Out-File $logFile -Append -Encoding UTF8

# Check Windows event log for errors
"=== Event Log Errors ===" | Out-File $logFile -Append -Encoding UTF8
try {
    $events = Get-WinEvent -FilterHashtable @{LogName='Application';Level=2} -MaxEvents 20 -ErrorAction SilentlyContinue
    $events | Out-File $logFile -Append -Encoding UTF8
} catch {
    "No events found: $_" | Out-File $logFile -Append -Encoding UTF8
}

# Try to run the exe directly and capture output
"=== Direct Exe Run Test ===" | Out-File $logFile -Append -Encoding UTF8
try {
    $process = Start-Process -FilePath $pubPath -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    if (!$process.HasExited) {
        "Application started successfully (PID: $($process.Id))" | Out-File $logFile -Append -Encoding UTF8
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    } else {
        "Application exited with code: $($process.ExitCode)" | Out-File $logFile -Append -Encoding UTF8
    }
} catch {
    "ERROR starting app: $_" | Out-File $logFile -Append -Encoding UTF8
}

"=== Debug Script Completed ===" | Out-File $logFile -Append -Encoding UTF8