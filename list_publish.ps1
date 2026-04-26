$items = Get-ChildItem 'E:\Atest1.0\AudioStudio\AudioStudio\publish' -ErrorAction SilentlyContinue
$items | ForEach-Object {
    $_.Name + "|" + $_.Length
} | Out-File 'E:\Atest1.0\AudioStudio\publish_items.txt' -Encoding UTF8
Write-Host "Done. Count: $($items.Count)"
