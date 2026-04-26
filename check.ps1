# Check publish folder
$count = (Get-ChildItem 'E:\Atest1.0\AudioStudio\AudioStudio\publish' -File).Count
Write-Host "File count: $count"
if ($count -gt 0) {
    Get-ChildItem 'E:\Atest1.0\AudioStudio\AudioStudio\publish' -File | Select-Object Name, Length
}
