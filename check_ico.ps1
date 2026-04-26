$src = Get-Item "C:\Users\Admin\Desktop\app.ico"
$dst = Get-Item "E:\Atest1.0\AudioStudio\AudioStudio\app.ico"
Write-Host "Source Size: $($src.Length)"
Write-Host "Project Size: $($dst.Length)"
if ($src.Length -eq $dst.Length) {
    Write-Host "SIZES MATCH - Copy succeeded"
} else {
    Write-Host "SIZES DIFFER - Copy failed"
}
