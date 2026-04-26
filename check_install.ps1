param([string]$ExePath="C:\Program Files\AudioStudio Demo\AudioStudio.exe",[string]$LogPath="$env:TEMP\audiostudio_debug.txt")

$log="=== AudioStudio Debug ===`n"
$log+="Time: $(Get-Date)`n"
$log+="ExePath: $ExePath`n"

# Check installed location
if(Test-Path $ExePath){
 $log+="EXE FOUND at $ExePath`n"
 $size=(Get-Item $ExePath).Length
 $log+="EXE Size: $size bytes`n"
 
 # List all files in install folder
 $installDir=Split-Path $ExePath -Parent
 $log+="`n=== Files in Install Folder ===`n"
 $allFiles=Get-ChildItem $installDir -Recurse -File -ErrorAction SilentlyContinue
 $log+="Total files: $($allFiles.Count)`n"
 foreach($f in $allFiles){$log+="  $($f.Name) ($($f.Length) bytes)`n"}
}else{
 $log+="EXE NOT FOUND at $ExePath`n"
 # Try to find
 $regApps=Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue | Where-Object{$_.DisplayName -like "*Audio*"}
 if($regApps){
  foreach($app in $regApps){
   $log+="Registry: $($app.DisplayName) -> $($app.InstallLocation)`n"
  }
 }
}

# Check .NET 10 runtime
$log+="`n=== .NET Runtime Check ===`n"
$netVersions=Get-ChildItem "HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions" -ErrorAction SilentlyContinue
foreach($v in $netVersions){
 $log+="dotnet: $($v.PSChildName)`n"
}

# Try to run app and capture error
$log+="`n=== Try to Run ===`n"
$errLog="$env:TEMP\app_err.log"
try{
 $proc=Start-Process $ExePath -PassThru -RedirectStandardError $errLog -WindowStyle Hidden
 Start-Sleep -Seconds 5
 if($proc.HasExited){
  $log+="EXITED with code: $($proc.ExitCode)`n"
  if(Test-Path $errLog){
   $log+="STDERR: $(Get-Content $errLog -Raw)`n"
  }
 }else{
  $log+="RUNNING (PID: $($proc.Id))`n"
  Stop-Process $proc.Id -Force -ErrorAction SilentlyContinue
 }
}catch{
 $log+="ERROR: $_`n"
}

# Event viewer errors
$log+="`n=== Recent Errors in Event Log ===`n"
try{
 $errs=Get-WinEvent -FilterHashtable @{LogName='Application';Level=2} -MaxEvents 3 -ErrorAction SilentlyContinue | Where-Object{$_.TimeCreated -gt (Get-Date).AddMinutes(-10)}
 foreach($e in $errs){
  $log+="$($e.TimeCreated): $($e.Message.Substring(0,[Math]::Min(500,$e.Message.Length)))`n---`n"
 }
}catch{}

$log|Out-File $LogPath -Encoding UTF8
Write-Host $log